package io.github.fullmetalsonic.roamadb.model

data class LocalAdbEndpoint(
    val connectPort: Int,
) {
    init {
        require(connectPort in 1..65535) { "Wireless ADB connect port is outside the TCP range." }
    }
}
