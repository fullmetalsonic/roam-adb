package io.github.fullmetalsonic.roamadb.model

enum class ConnectionMode {
    DIRECT_HOME_GATEWAY,
    EMBEDDED_SECURE_NETWORK,
    EXISTING_VPN_ADB_ONLY;

    companion object {
        fun fromStorage(value: String): ConnectionMode? = entries.firstOrNull { it.name == value }
    }
}

data class GatewayProfile(
    val host: String,
    val port: Int,
    val certificateFingerprint: String,
    val deviceId: String,
    val deviceName: String,
    val connectionMode: ConnectionMode,
) {
    init {
        require(host.isNotBlank()) { "Gateway host is required." }
        require(port in 1..65535) { "Gateway port is outside the TCP range." }
        require(deviceId.matches(Regex("[A-Za-z0-9_-]{1,128}"))) { "Device ID is invalid." }
        require(deviceName.isNotBlank() && deviceName.length <= 80) { "Device name is invalid." }
    }

    val normalizedFingerprint: String = normalizeFingerprint(certificateFingerprint)

    companion object {
        fun normalizeFingerprint(value: String): String {
            val normalized = value.replace(":", "").replace(" ", "").uppercase()
            require(normalized.matches(Regex("[0-9A-F]{64}"))) {
                "Gateway fingerprint must be a SHA-256 hexadecimal value."
            }
            return normalized
        }
    }
}
