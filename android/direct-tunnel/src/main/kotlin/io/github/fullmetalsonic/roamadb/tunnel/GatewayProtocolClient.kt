package io.github.fullmetalsonic.roamadb.tunnel

import android.annotation.SuppressLint
import io.github.fullmetalsonic.roamadb.model.GatewayProfile
import io.github.fullmetalsonic.roamadb.security.PhoneIdentityStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.selects.select
import kotlinx.coroutines.supervisorScope
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.EOFException
import java.io.InputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.InetAddress
import java.net.Socket
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

class GatewayProtocolClient(
    private val identityStore: PhoneIdentityStore,
) {
    suspend fun register(profile: GatewayProfile, registrationCode: String) = withContext(Dispatchers.IO) {
        require(registrationCode.matches(Regex("[0-9]{6}"))) {
            "Registration code must contain six digits."
        }

        openConnection(profile).use { connection ->
            connection.write(
                JSONObject()
                    .put("type", "register")
                    .put("protocolVersion", PROTOCOL_VERSION)
                    .put("deviceId", profile.deviceId)
                    .put("deviceName", profile.deviceName)
                    .put("publicKey", identityStore.publicKeySpkiBase64())
                    .put("code", registrationCode),
            )
            val response = connection.read()
            if (response.optString("type") != "registered" || !response.optBoolean("success")) {
                throw GatewayProtocolException(response.optString("message", "registration_rejected"))
            }
        }
    }

    suspend fun authenticate(profile: GatewayProfile): GatewaySession = withContext(Dispatchers.IO) {
        val connection = openConnection(profile)
        try {
            connection.write(
                JSONObject()
                    .put("type", "hello")
                    .put("protocolVersion", PROTOCOL_VERSION)
                    .put("deviceId", profile.deviceId),
            )
            val challenge = connection.read()
            if (challenge.optString("type") != "challenge") {
                throw GatewayProtocolException(challenge.optString("message", "challenge_missing"))
            }

            val nonce = android.util.Base64.decode(
                challenge.getString("nonce"),
                android.util.Base64.DEFAULT,
            )
            val signature = identityStore.sign(nonce)
            connection.write(
                JSONObject()
                    .put("type", "authenticate")
                    .put("protocolVersion", PROTOCOL_VERSION)
                    .put("deviceId", profile.deviceId)
                    .put(
                        "signature",
                        android.util.Base64.encodeToString(signature, android.util.Base64.NO_WRAP),
                    ),
            )
            val authenticated = connection.read()
            if (authenticated.optString("type") != "authenticated" || !authenticated.optBoolean("success")) {
                throw GatewayProtocolException(authenticated.optString("message", "authentication_rejected"))
            }
            GatewaySession(connection)
        } catch (throwable: Throwable) {
            connection.close()
            throw throwable
        }
    }

    private fun openConnection(profile: GatewayProfile): JsonTlsConnection {
        val expectedFingerprint = hexToBytes(profile.normalizedFingerprint)
        val trustManager = FingerprintTrustManager(expectedFingerprint)
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf(trustManager), SecureRandom())
        }
        val socket = sslContext.socketFactory.createSocket() as SSLSocket
        socket.enabledProtocols = socket.supportedProtocols
            .filter { it == "TLSv1.3" || it == "TLSv1.2" }
            .toTypedArray()
        socket.soTimeout = SOCKET_TIMEOUT_MILLIS
        socket.connect(InetSocketAddress(profile.host, profile.port), SOCKET_TIMEOUT_MILLIS)
        socket.startHandshake()
        return JsonTlsConnection(socket)
    }

    private fun hexToBytes(value: String): ByteArray =
        ByteArray(value.length / 2) { index -> value.substring(index * 2, index * 2 + 2).toInt(16).toByte() }

    // Registration supplies this exact SHA-256 certificate fingerprint out of band.
    // CA/hostname validation is intentionally replaced by fixed-time certificate pinning.
    @SuppressLint("CustomX509TrustManager")
    private class FingerprintTrustManager(
        private val expectedFingerprint: ByteArray,
    ) : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            throw CertificateException("Client certificate validation is not supported here.")
        }

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            val leaf = chain?.firstOrNull() ?: throw CertificateException("Gateway certificate is missing.")
            val actual = MessageDigest.getInstance("SHA-256").digest(leaf.encoded)
            if (!MessageDigest.isEqual(actual, expectedFingerprint)) {
                throw CertificateException("Gateway certificate fingerprint does not match.")
            }
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    companion object {
        private const val PROTOCOL_VERSION = 1
        private const val SOCKET_TIMEOUT_MILLIS = 15_000
    }
}

class GatewaySession internal constructor(
    private val connection: JsonTlsConnection,
) : Closeable {
    private var rawRelayActive = false

    fun ping(): Boolean {
        connection.write(JSONObject().put("type", "ping").put("protocolVersion", 1))
        return connection.read().optString("type") == "pong"
    }

    fun publishRelay(relayKind: String): PublishedRelay {
        require(relayKind == RELAY_CONNECT || relayKind == RELAY_PAIRING) { "Unsupported relay kind." }
        connection.write(
            JSONObject()
                .put("type", "publish_relay")
                .put("protocolVersion", 1)
                .put("relayKind", relayKind),
        )
        val response = connection.read()
        if (response.optString("type") != "relay_published" || !response.optBoolean("success")) {
            throw GatewayProtocolException(response.optString("message", "relay_publish_failed"))
        }
        connection.disableReadTimeout()
        return PublishedRelay(
            relayKind = relayKind,
            gatewayLoopbackPort = response.getInt("relayPort"),
        )
    }

    suspend fun awaitAndRunRelay(
        publishedRelay: PublishedRelay,
        localAdbPort: Int,
        onConnected: () -> Unit,
    ) = withContext(Dispatchers.IO) {
        require(localAdbPort in 1..65535) { "Wireless ADB connect port is invalid." }
        val start = connection.read()
        if (
            start.optString("type") != "relay_start" ||
            start.optString("relayKind") != publishedRelay.relayKind
        ) {
            throw GatewayProtocolException(start.optString("message", "relay_start_missing"))
        }

        val localAdbSocket = Socket()
        try {
            localAdbSocket.connect(
                InetSocketAddress(InetAddress.getLoopbackAddress(), localAdbPort),
                LOCAL_ADB_CONNECT_TIMEOUT_MILLIS,
            )
        } catch (throwable: Throwable) {
            localAdbSocket.close()
            connection.write(
                JSONObject()
                    .put("type", "relay_error")
                    .put("protocolVersion", 1)
                    .put("relayKind", publishedRelay.relayKind)
                    .put("message", "local_adb_unreachable"),
            )
            throw throwable
        }

        connection.write(
            JSONObject()
                .put("type", "relay_ready")
                .put("protocolVersion", 1)
                .put("relayKind", publishedRelay.relayKind),
        )
        rawRelayActive = true
        onConnected()
        connection.relayRaw(localAdbSocket)
    }

    override fun close() {
        if (!rawRelayActive) {
            runCatching {
                connection.write(JSONObject().put("type", "close").put("protocolVersion", 1))
            }
        }
        connection.close()
    }

    companion object {
        const val RELAY_CONNECT = "connect"
        const val RELAY_PAIRING = "pairing"
        private const val LOCAL_ADB_CONNECT_TIMEOUT_MILLIS = 5_000
    }
}

data class PublishedRelay(
    val relayKind: String,
    val gatewayLoopbackPort: Int,
)

internal class JsonTlsConnection(
    private val socket: SSLSocket,
) : Closeable {
    private val input = socket.inputStream
    private val output = BufferedOutputStream(socket.outputStream)

    fun write(message: JSONObject) {
        val payload = (message.toString() + "\n").toByteArray(Charsets.UTF_8)
        require(payload.size <= MAXIMUM_MESSAGE_BYTES) { "Protocol message is too large." }
        output.write(payload)
        output.flush()
    }

    fun read(): JSONObject {
        val payload = readBoundedLine(input) ?: throw EOFException("Gateway closed the connection.")
        return JSONObject(payload.toString(Charsets.UTF_8))
    }

    fun disableReadTimeout() {
        socket.soTimeout = 0
    }

    suspend fun relayRaw(localAdbSocket: Socket) = supervisorScope {
        val phoneToAdb = async(Dispatchers.IO) {
            socket.inputStream.copyTo(localAdbSocket.outputStream, RAW_COPY_BUFFER_BYTES)
        }
        val adbToPhone = async(Dispatchers.IO) {
            localAdbSocket.inputStream.copyTo(socket.outputStream, RAW_COPY_BUFFER_BYTES)
        }

        try {
            try {
                select<Unit> {
                    phoneToAdb.onAwait { }
                    adbToPhone.onAwait { }
                }
            } catch (_: IOException) {
                // Either the PC-side ADB socket or local adbd closed the relay.
            }
        } finally {
            localAdbSocket.close()
            socket.close()
            phoneToAdb.cancelAndJoin()
            adbToPhone.cancelAndJoin()
        }
    }

    override fun close() {
        socket.close()
    }

    private fun readBoundedLine(input: InputStream): ByteArray? {
        val bytes = ByteArrayOutputStream()
        while (bytes.size() < MAXIMUM_MESSAGE_BYTES) {
            val value = input.read()
            if (value == -1) return if (bytes.size() == 0) null else throw EOFException("Partial message.")
            if (value == '\n'.code) return bytes.toByteArray()
            bytes.write(value)
        }
        throw GatewayProtocolException("message_too_large")
    }

    companion object {
        private const val MAXIMUM_MESSAGE_BYTES = 65_536
        private const val RAW_COPY_BUFFER_BYTES = 65_536
    }
}

class GatewayProtocolException(message: String) : Exception(message)
