package io.github.fullmetalsonic.roamadb.model

import java.net.URI
import java.net.URLDecoder

data class RegistrationQrPayload(
    val host: String,
    val port: Int,
    val fingerprint: String,
    val registrationCode: String,
    val expiresAtEpochSeconds: Long,
) {
    companion object {
        private const val SCHEME = "roamadb"
        private const val AUTHORITY = "register"
        private const val VERSION = "1"
        private const val MODE = "existing-vpn-adb-only"
        private const val MAXIMUM_FUTURE_SECONDS = 180L

        fun parse(
            rawValue: String,
            nowEpochSeconds: Long = System.currentTimeMillis() / 1_000L,
        ): RegistrationQrPayload {
            val uri = runCatching { URI(rawValue.trim()) }
                .getOrElse { throw IllegalArgumentException("Registration QR is not a valid URI.") }
            require(uri.scheme == SCHEME && uri.host == AUTHORITY && uri.path.isNullOrEmpty()) {
                "This QR was not created by RoamADB Gateway."
            }

            val query = parseQuery(uri.rawQuery)
            val requiredKeys = setOf("v", "host", "port", "fingerprint", "code", "mode", "expires")
            require(query.keys == requiredKeys) { "Registration QR fields are missing or unsupported." }
            require(query.getValue("v") == VERSION) { "Registration QR version is unsupported." }
            require(query.getValue("mode") == MODE) { "Registration QR connection mode is unsupported." }

            val host = query.getValue("host")
            require(isTailnetIpv4(host)) { "Registration QR does not contain a Tailscale IPv4 address." }
            val port = query.getValue("port").toIntOrNull()
                ?: throw IllegalArgumentException("Registration QR Gateway port is invalid.")
            require(port in 1..65_535) { "Registration QR Gateway port is invalid." }
            val fingerprint = GatewayProfile.normalizeFingerprint(query.getValue("fingerprint"))
            val code = query.getValue("code")
            require(code.matches(Regex("[0-9]{6}"))) { "Registration QR code is invalid." }
            val expires = query.getValue("expires").toLongOrNull()
                ?: throw IllegalArgumentException("Registration QR expiry is invalid.")
            require(expires > nowEpochSeconds) { "Registration QR has expired. Create a new code on the PC." }
            require(expires <= nowEpochSeconds + MAXIMUM_FUTURE_SECONDS) {
                "Registration QR expiry is outside the allowed range."
            }

            return RegistrationQrPayload(host, port, fingerprint, code, expires)
        }

        private fun parseQuery(rawQuery: String?): Map<String, String> {
            require(!rawQuery.isNullOrBlank()) { "Registration QR has no data." }
            val result = linkedMapOf<String, String>()
            rawQuery.split('&').forEach { part ->
                val pieces = part.split('=', limit = 2)
                require(pieces.size == 2) { "Registration QR contains a malformed field." }
                val key = decode(pieces[0])
                val value = decode(pieces[1])
                require(key !in result) { "Registration QR contains a duplicate field." }
                result[key] = value
            }
            return result
        }

        private fun decode(value: String): String = URLDecoder.decode(value, Charsets.UTF_8.name())

        private fun isTailnetIpv4(value: String): Boolean {
            val octets = value.split('.').map { it.toIntOrNull() ?: return false }
            return octets.size == 4 &&
                octets.all { it in 0..255 } &&
                octets[0] == 100 && octets[1] in 64..127
        }
    }
}
