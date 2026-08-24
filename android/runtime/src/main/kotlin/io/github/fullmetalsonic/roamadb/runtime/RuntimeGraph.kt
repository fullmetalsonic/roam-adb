package io.github.fullmetalsonic.roamadb.runtime

import android.content.Context
import io.github.fullmetalsonic.roamadb.connection.ConnectionController
import io.github.fullmetalsonic.roamadb.security.GatewayProfileStore
import io.github.fullmetalsonic.roamadb.security.LocalAdbEndpointStore
import io.github.fullmetalsonic.roamadb.security.PhoneIdentityStore
import io.github.fullmetalsonic.roamadb.tunnel.GatewayProtocolClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

object RuntimeGraph {
    @Volatile
    private var initialized = false

    lateinit var connectionController: ConnectionController
        private set

    lateinit var profileStore: GatewayProfileStore
        private set

    lateinit var localAdbEndpointStore: LocalAdbEndpointStore
        private set

    fun initialize(context: Context) {
        if (initialized) return
        synchronized(this) {
            if (initialized) return
            val applicationContext = context.applicationContext
            profileStore = GatewayProfileStore(applicationContext)
            localAdbEndpointStore = LocalAdbEndpointStore(applicationContext)
            val identity = PhoneIdentityStore()
            connectionController = ConnectionController(
                profileStore = profileStore,
                localAdbEndpointStore = localAdbEndpointStore,
                protocolClient = GatewayProtocolClient(identity),
                networkRouteChecker = AndroidNetworkRouteChecker(applicationContext),
                scope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
            )
            initialized = true
        }
    }
}
