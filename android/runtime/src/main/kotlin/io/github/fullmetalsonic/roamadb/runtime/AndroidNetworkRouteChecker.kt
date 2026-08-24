package io.github.fullmetalsonic.roamadb.runtime

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import io.github.fullmetalsonic.roamadb.connection.NetworkRouteChecker

class AndroidNetworkRouteChecker(context: Context) : NetworkRouteChecker {
    private val connectivityManager = context.getSystemService(ConnectivityManager::class.java)

    override fun hasActiveVpnTransport(): Boolean {
        val activeNetwork = connectivityManager.activeNetwork ?: return false
        return connectivityManager.getNetworkCapabilities(activeNetwork)
            ?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true
    }
}
