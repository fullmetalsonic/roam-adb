package io.github.fullmetalsonic.roamadb.connection

import io.github.fullmetalsonic.roamadb.model.ConnectionState
import io.github.fullmetalsonic.roamadb.model.ConnectionMode
import io.github.fullmetalsonic.roamadb.model.GatewayProfile
import io.github.fullmetalsonic.roamadb.model.LocalAdbEndpoint
import io.github.fullmetalsonic.roamadb.security.GatewayProfileStore
import io.github.fullmetalsonic.roamadb.security.LocalAdbEndpointStore
import io.github.fullmetalsonic.roamadb.tunnel.GatewayProtocolClient
import io.github.fullmetalsonic.roamadb.tunnel.GatewaySession
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class ConnectionController(
    private val profileStore: GatewayProfileStore,
    private val localAdbEndpointStore: LocalAdbEndpointStore,
    private val protocolClient: GatewayProtocolClient,
    private val networkRouteChecker: NetworkRouteChecker,
    private val scope: CoroutineScope,
) {
    private val mutableState = MutableStateFlow<ConnectionState>(
        if (profileStore.load() == null) ConnectionState.SetupRequired else ConnectionState.Off,
    )
    private var connectionJob: Job? = null
    private var session: GatewaySession? = null
    private var operationId: Long = 0

    val state: StateFlow<ConnectionState> = mutableState.asStateFlow()

    suspend fun register(profile: GatewayProfile, registrationCode: String) {
        check(!state.value.isRunning) { "Stop the active connection before registration." }
        mutableState.value = ConnectionState.Starting
        runCatching {
            validateConnectionMode(profile)
            protocolClient.register(profile, registrationCode)
            profileStore.save(profile)
        }.onSuccess {
            mutableState.value = ConnectionState.Off
        }.onFailure { throwable ->
            mutableState.value = ConnectionState.Error(
                code = "registration_failed",
                detail = throwable.message ?: throwable::class.java.simpleName,
            )
        }.getOrThrow()
    }

    fun start() {
        if (connectionJob?.isActive == true) return
        val profile = profileStore.load()
        if (profile == null) {
            mutableState.value = ConnectionState.SetupRequired
            return
        }
        val localAdbEndpoint = localAdbEndpointStore.load()
        if (localAdbEndpoint == null) {
            mutableState.value = ConnectionState.Error(
                code = "wireless_adb_port_required",
                detail = "Enter the connect port shown on Android's Wireless debugging screen.",
            )
            return
        }

        mutableState.value = ConnectionState.Starting
        val currentOperation = ++operationId
        connectionJob = scope.launch {
            var reachedReady = false
            while (currentCoroutineContext().isActive && currentOperation == operationId) {
                var openedSession: GatewaySession? = null
                try {
                    mutableState.value = if (reachedReady) {
                        ConnectionState.Recovering
                    } else {
                        ConnectionState.Starting
                    }
                    validateConnectionMode(profile)
                    openedSession = protocolClient.authenticate(profile)
                    if (currentOperation != operationId) {
                        openedSession.close()
                        return@launch
                    }

                    session = openedSession
                    val publishedRelay = openedSession.publishRelay(GatewaySession.RELAY_CONNECT)
                    reachedReady = true
                    mutableState.value = ConnectionState.Ready
                    openedSession.awaitAndRunRelay(
                        publishedRelay = publishedRelay,
                        localAdbPort = localAdbEndpoint.connectPort,
                    ) {
                        if (currentOperation == operationId) {
                            mutableState.value = ConnectionState.PcConnected(
                                "127.0.0.1:${publishedRelay.gatewayLoopbackPort}",
                            )
                        }
                    }
                } catch (throwable: CancellationException) {
                    throw throwable
                } catch (throwable: Throwable) {
                    if (currentOperation == operationId && !reachedReady) {
                        mutableState.value = ConnectionState.Error(
                            code = "gateway_connection_failed",
                            detail = throwable.message ?: throwable::class.java.simpleName,
                        )
                        return@launch
                    }
                } finally {
                    if (session === openedSession) {
                        session = null
                    }
                    openedSession?.close()
                }

                if (currentOperation == operationId) {
                    mutableState.value = ConnectionState.Recovering
                    delay(RECONNECT_DELAY_MILLIS)
                }
            }
        }
    }

    fun stop() {
        operationId++
        connectionJob?.cancel()
        connectionJob = null
        session?.close()
        session = null
        mutableState.value = if (profileStore.load() == null) {
            ConnectionState.SetupRequired
        } else {
            ConnectionState.Off
        }
    }

    fun clearRegistration() {
        stop()
        profileStore.clear()
        localAdbEndpointStore.clear()
        mutableState.value = ConnectionState.SetupRequired
    }

    fun saveLocalAdbConnectPort(port: Int) {
        check(!state.value.isRunning) { "Stop the active connection before changing the ADB port." }
        localAdbEndpointStore.save(LocalAdbEndpoint(port))
        mutableState.value = if (profileStore.load() == null) {
            ConnectionState.SetupRequired
        } else {
            ConnectionState.Off
        }
    }

    fun reportInputError(code: String, detail: String) {
        if (!state.value.isRunning) {
            mutableState.value = ConnectionState.Error(code, detail)
        }
    }

    private fun validateConnectionMode(profile: GatewayProfile) {
        when (profile.connectionMode) {
            ConnectionMode.DIRECT_HOME_GATEWAY -> Unit
            ConnectionMode.EMBEDDED_SECURE_NETWORK -> error(
                "The built-in secure-network mode is not implemented in this build.",
            )
            ConnectionMode.EXISTING_VPN_ADB_ONLY -> check(networkRouteChecker.hasActiveVpnTransport()) {
                "No active Android VPN was detected. Connect the Tailscale app, then try again. " +
                    "Also make sure RoamADB is not excluded by VPN split tunneling."
            }
        }
    }

    companion object {
        private const val RECONNECT_DELAY_MILLIS = 1_500L
    }
}
