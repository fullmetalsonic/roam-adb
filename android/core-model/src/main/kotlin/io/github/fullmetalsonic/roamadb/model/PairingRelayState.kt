package io.github.fullmetalsonic.roamadb.model

sealed interface PairingRelayState {
    data object Off : PairingRelayState
    data object Starting : PairingRelayState
    data class Ready(val pcLoopbackPort: Int) : PairingRelayState
    data object PcConnected : PairingRelayState
    data class Error(val detail: String) : PairingRelayState

    val isRunning: Boolean
        get() = this is Starting || this is Ready || this is PcConnected
}
