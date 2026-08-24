package io.github.fullmetalsonic.roamadb

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import io.github.fullmetalsonic.roamadb.model.ConnectionState
import io.github.fullmetalsonic.roamadb.model.ConnectionMode
import io.github.fullmetalsonic.roamadb.model.PairingRelayState
import io.github.fullmetalsonic.roamadb.model.RegistrationQrPayload
import io.github.fullmetalsonic.roamadb.runtime.RoamAdbService
import io.github.fullmetalsonic.roamadb.runtime.RuntimeGraph
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

class MainViewModel(application: Application) : AndroidViewModel(application) {
    val connectionState: StateFlow<ConnectionState> = RuntimeGraph.connectionController.state
    val pairingState: StateFlow<PairingRelayState> = RuntimeGraph.connectionController.pairingState
    private val initialProfile = RuntimeGraph.profileStore.load()
    private val mutablePcRegistered = MutableStateFlow(initialProfile != null)
    val pcRegistered: StateFlow<Boolean> = mutablePcRegistered.asStateFlow()
    private val mutableConnectionMode = MutableStateFlow(
        initialProfile?.connectionMode ?: ConnectionMode.EXISTING_VPN_ADB_ONLY,
    )
    val connectionMode: StateFlow<ConnectionMode> = mutableConnectionMode.asStateFlow()
    private val mutableAdbConnectPort = MutableStateFlow(
        RuntimeGraph.localAdbEndpointStore.load()?.connectPort?.toString().orEmpty(),
    )
    val adbConnectPort: StateFlow<String> = mutableAdbConnectPort.asStateFlow()
    private val mutableRegistrationDraft = MutableStateFlow<RegistrationDraft?>(null)
    val registrationDraft: StateFlow<RegistrationDraft?> = mutableRegistrationDraft.asStateFlow()

    fun register(
        host: String,
        portText: String,
        fingerprint: String,
        registrationCode: String,
    ) {
        val port = portText.toIntOrNull()
            ?: return reportInputError("invalid_port", "Gateway port must be a number.")
        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
        val profile = runCatching {
            RuntimeGraph.profileStore.createCandidate(
                host = host.trim(),
                port = port,
                fingerprint = fingerprint,
                deviceName = deviceName,
                connectionMode = mutableConnectionMode.value,
            )
        }.getOrElse { throwable ->
            reportInputError("invalid_profile", throwable.message ?: "Invalid Gateway profile.")
            return
        }

        viewModelScope.launch {
            runCatching {
                RuntimeGraph.connectionController.register(profile, registrationCode.trim())
            }.onSuccess {
                mutablePcRegistered.value = true
            }
        }
    }

    fun start() = RoamAdbService.start(getApplication())

    fun stop() = RoamAdbService.stop(getApplication())

    fun applyRegistrationQr(rawValue: String) {
        runCatching { RegistrationQrPayload.parse(rawValue) }
            .onSuccess { payload ->
                mutableConnectionMode.value = ConnectionMode.EXISTING_VPN_ADB_ONLY
                mutableRegistrationDraft.value = RegistrationDraft(
                    host = payload.host,
                    port = payload.port.toString(),
                    fingerprint = payload.fingerprint,
                    registrationCode = payload.registrationCode,
                )
            }
            .onFailure { throwable ->
                reportInputError("invalid_registration_qr", throwable.message ?: "Invalid registration QR.")
            }
    }

    fun reportQrScannerError(detail: String) = reportInputError("qr_scanner_failed", detail)

    fun selectConnectionMode(mode: ConnectionMode) {
        if (!mutablePcRegistered.value && !connectionState.value.isRunning) {
            mutableConnectionMode.value = mode
        }
    }

    fun saveAdbConnectPort(portText: String) {
        val port = portText.toIntOrNull()
            ?: return reportInputError("invalid_adb_port", "Wireless ADB connect port must be a number.")
        runCatching {
            RuntimeGraph.connectionController.saveLocalAdbConnectPort(port)
        }.onSuccess {
            mutableAdbConnectPort.value = port.toString()
        }.onFailure { throwable ->
            reportInputError("invalid_adb_port", throwable.message ?: "Invalid Wireless ADB connect port.")
        }
    }

    fun startPairing(portText: String) {
        val port = portText.toIntOrNull()
            ?: return reportInputError("invalid_pairing_port", "Wireless ADB pairing port must be a number.")
        runCatching { RuntimeGraph.connectionController.startPairing(port) }
            .onFailure { throwable ->
                reportInputError("pairing_relay_failed", throwable.message ?: "Could not start pairing relay.")
            }
    }

    fun stopPairing() = RuntimeGraph.connectionController.stopPairing()

    fun clearRegistration() {
        RuntimeGraph.connectionController.clearRegistration()
        mutablePcRegistered.value = false
        mutableAdbConnectPort.value = ""
        mutableRegistrationDraft.value = null
        mutableConnectionMode.value = ConnectionMode.EXISTING_VPN_ADB_ONLY
    }

    fun reportNotificationPermissionDenied() = reportInputError(
        "notification_permission_denied",
        getApplication<Application>().getString(R.string.notification_permission_denied),
    )

    private fun reportInputError(code: String, detail: String) {
        RuntimeGraph.connectionController.reportInputError(code, detail)
    }
}

data class RegistrationDraft(
    val host: String,
    val port: String,
    val fingerprint: String,
    val registrationCode: String,
)
