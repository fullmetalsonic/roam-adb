package io.github.fullmetalsonic.roamadb.model

sealed interface ConnectionState {
    val isRunning: Boolean

    data object SetupRequired : ConnectionState {
        override val isRunning: Boolean = false
    }

    data object Off : ConnectionState {
        override val isRunning: Boolean = false
    }

    data object Starting : ConnectionState {
        override val isRunning: Boolean = true
    }

    data object Ready : ConnectionState {
        override val isRunning: Boolean = true
    }

    data class PcConnected(val gatewayName: String) : ConnectionState {
        override val isRunning: Boolean = true
    }

    data object Recovering : ConnectionState {
        override val isRunning: Boolean = true
    }

    data class Error(val code: String, val detail: String) : ConnectionState {
        override val isRunning: Boolean = false
    }
}
