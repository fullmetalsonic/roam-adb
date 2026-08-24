package io.github.fullmetalsonic.roamadb.connection

fun interface NetworkRouteChecker {
    fun hasActiveVpnTransport(): Boolean
}
