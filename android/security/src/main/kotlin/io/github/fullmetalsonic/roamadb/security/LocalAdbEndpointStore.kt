package io.github.fullmetalsonic.roamadb.security

import android.content.Context
import io.github.fullmetalsonic.roamadb.model.LocalAdbEndpoint

class LocalAdbEndpointStore(context: Context) {
    private val preferences = context.getSharedPreferences("local-adb-endpoint-v1", Context.MODE_PRIVATE)

    fun load(): LocalAdbEndpoint? {
        val connectPort = preferences.getInt("connectPort", 0)
        return runCatching { LocalAdbEndpoint(connectPort) }.getOrNull()
    }

    fun save(endpoint: LocalAdbEndpoint) {
        preferences.edit().putInt("connectPort", endpoint.connectPort).apply()
    }

    fun clear() {
        preferences.edit().clear().apply()
    }
}
