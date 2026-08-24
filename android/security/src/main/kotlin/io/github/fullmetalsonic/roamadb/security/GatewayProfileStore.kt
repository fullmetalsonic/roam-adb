package io.github.fullmetalsonic.roamadb.security

import android.content.Context
import io.github.fullmetalsonic.roamadb.model.ConnectionMode
import io.github.fullmetalsonic.roamadb.model.GatewayProfile
import java.util.UUID

class GatewayProfileStore(context: Context) {
    private val preferences = context.getSharedPreferences("gateway-profile-v1", Context.MODE_PRIVATE)

    fun load(): GatewayProfile? {
        val host = preferences.getString("host", null) ?: return null
        val fingerprint = preferences.getString("fingerprint", null) ?: return null
        val deviceId = preferences.getString("deviceId", null) ?: return null
        val deviceName = preferences.getString("deviceName", null) ?: return null
        val port = preferences.getInt("port", 0)
        val storedMode = preferences.getString("connectionMode", null)
        val connectionMode = if (storedMode == null) {
            ConnectionMode.DIRECT_HOME_GATEWAY
        } else {
            ConnectionMode.fromStorage(storedMode) ?: return null
        }
        return runCatching {
            GatewayProfile(host, port, fingerprint, deviceId, deviceName, connectionMode)
        }.getOrNull()
    }

    fun createCandidate(
        host: String,
        port: Int,
        fingerprint: String,
        deviceName: String,
        connectionMode: ConnectionMode,
    ): GatewayProfile {
        val existingDeviceId = preferences.getString("deviceId", null)
            ?: "android-${UUID.randomUUID()}"
        return GatewayProfile(host, port, fingerprint, existingDeviceId, deviceName, connectionMode)
    }

    fun save(profile: GatewayProfile) {
        preferences.edit()
            .putString("host", profile.host)
            .putInt("port", profile.port)
            .putString("fingerprint", profile.normalizedFingerprint)
            .putString("deviceId", profile.deviceId)
            .putString("deviceName", profile.deviceName)
            .putString("connectionMode", profile.connectionMode.name)
            .apply()
    }

    fun clear() {
        preferences.edit().clear().apply()
    }
}
