package io.github.fullmetalsonic.roamadb

import android.Manifest
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import io.github.fullmetalsonic.roamadb.model.ConnectionState
import io.github.fullmetalsonic.roamadb.model.ConnectionMode
import io.github.fullmetalsonic.roamadb.model.PairingRelayState
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode

class MainActivity : ComponentActivity() {
    private val mainViewModel: MainViewModel by viewModels()
    private val qrScanner by lazy {
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
            .enableAutoZoom()
            .build()
        GmsBarcodeScanning.getClient(this, options)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            MaterialTheme {
                RoamAdbScreen(
                    mainViewModel = mainViewModel,
                    onScanQr = {
                        qrScanner.startScan()
                            .addOnSuccessListener { barcode ->
                                val rawValue = barcode.rawValue
                                if (rawValue.isNullOrBlank()) {
                                    mainViewModel.reportQrScannerError(getString(R.string.qr_scan_empty))
                                } else {
                                    mainViewModel.applyRegistrationQr(rawValue)
                                }
                            }
                            .addOnFailureListener { throwable ->
                                mainViewModel.reportQrScannerError(
                                    throwable.message ?: getString(R.string.qr_scan_failed),
                                )
                            }
                    },
                )
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun RoamAdbScreen(
    mainViewModel: MainViewModel = viewModel(),
    onScanQr: () -> Unit,
) {
    val state by mainViewModel.connectionState.collectAsStateWithLifecycle()
    val pcRegistered by mainViewModel.pcRegistered.collectAsStateWithLifecycle()
    val connectionMode by mainViewModel.connectionMode.collectAsStateWithLifecycle()
    val savedAdbConnectPort by mainViewModel.adbConnectPort.collectAsStateWithLifecycle()
    val registrationDraft by mainViewModel.registrationDraft.collectAsStateWithLifecycle()
    val pairingState by mainViewModel.pairingState.collectAsStateWithLifecycle()
    val notificationPermission = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        if (granted) {
            mainViewModel.start()
        } else {
            mainViewModel.reportNotificationPermissionDenied()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text(text = "RoamADB")
                        Text(
                            text = androidx.compose.ui.res.stringResource(R.string.app_subtitle),
                            style = MaterialTheme.typography.labelMedium,
                        )
                    }
                },
            )
        },
    ) { contentPadding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(contentPadding)
                .verticalScroll(rememberScrollState())
                .padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            if (Build.VERSION.SDK_INT < 36) {
                WarningCard(androidx.compose.ui.res.stringResource(R.string.unsupported_android))
                return@Column
            }

            StatusCard(state)
            ConnectionButton(state, mainViewModel, notificationPermission::launch)

            if (!pcRegistered) {
                ConnectionModeCard(
                    selectedMode = connectionMode,
                    enabled = !state.isRunning,
                    onSelected = mainViewModel::selectConnectionMode,
                )
                RegistrationCard(
                    mainViewModel = mainViewModel,
                    registering = state is ConnectionState.Starting,
                    connectionMode = connectionMode,
                    registrationDraft = registrationDraft,
                    onScanQr = onScanQr,
                )
            } else {
                ActiveModeCard(connectionMode)
                AdbPortCard(
                    savedPort = savedAdbConnectPort,
                    enabled = !state.isRunning,
                    onSave = mainViewModel::saveAdbConnectPort,
                )
                PairingRelayCard(
                    pairingState = pairingState,
                    normalRelayRunning = state.isRunning,
                    onStart = mainViewModel::startPairing,
                    onStop = mainViewModel::stopPairing,
                )
                OutlinedButton(
                    onClick = mainViewModel::clearRegistration,
                    enabled = !state.isRunning,
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Text(androidx.compose.ui.res.stringResource(R.string.clear_registration))
                }
            }

            WarningCard(androidx.compose.ui.res.stringResource(R.string.spike_notice))
        }
    }
}

@Composable
private fun ConnectionModeCard(
    selectedMode: ConnectionMode,
    enabled: Boolean,
    onSelected: (ConnectionMode) -> Unit,
) {
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.connection_mode_title),
                style = MaterialTheme.typography.titleLarge,
            )
            Text(androidx.compose.ui.res.stringResource(R.string.connection_mode_description))
            ModeOption(
                mode = ConnectionMode.EXISTING_VPN_ADB_ONLY,
                selectedMode = selectedMode,
                enabled = enabled,
                title = androidx.compose.ui.res.stringResource(R.string.mode_existing_vpn_title),
                description = androidx.compose.ui.res.stringResource(R.string.mode_existing_vpn_description),
                onSelected = onSelected,
            )
            ModeOption(
                mode = ConnectionMode.DIRECT_HOME_GATEWAY,
                selectedMode = selectedMode,
                enabled = enabled,
                title = androidx.compose.ui.res.stringResource(R.string.mode_direct_title),
                description = androidx.compose.ui.res.stringResource(R.string.mode_direct_description),
                onSelected = onSelected,
            )
            ModeOption(
                mode = ConnectionMode.EMBEDDED_SECURE_NETWORK,
                selectedMode = selectedMode,
                enabled = enabled,
                title = androidx.compose.ui.res.stringResource(R.string.mode_embedded_title),
                description = androidx.compose.ui.res.stringResource(R.string.mode_embedded_description),
                onSelected = onSelected,
            )
        }
    }
}

@Composable
private fun ModeOption(
    mode: ConnectionMode,
    selectedMode: ConnectionMode,
    enabled: Boolean,
    title: String,
    description: String,
    onSelected: (ConnectionMode) -> Unit,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .selectable(
                selected = mode == selectedMode,
                enabled = enabled,
                role = Role.RadioButton,
                onClick = { onSelected(mode) },
            )
            .padding(vertical = 4.dp),
        verticalAlignment = Alignment.Top,
    ) {
        RadioButton(
            selected = mode == selectedMode,
            onClick = null,
            enabled = enabled,
        )
        Column(
            modifier = Modifier
                .weight(1f)
                .padding(top = 12.dp),
        ) {
            Text(text = title, style = MaterialTheme.typography.titleMedium)
            Text(text = description, style = MaterialTheme.typography.bodyMedium)
        }
    }
}

@Composable
private fun ActiveModeCard(mode: ConnectionMode) {
    val modeName = when (mode) {
        ConnectionMode.EXISTING_VPN_ADB_ONLY -> R.string.mode_existing_vpn_title
        ConnectionMode.DIRECT_HOME_GATEWAY -> R.string.mode_direct_title
        ConnectionMode.EMBEDDED_SECURE_NETWORK -> R.string.mode_embedded_title
    }
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(20.dp)) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.active_mode_title),
                style = MaterialTheme.typography.labelLarge,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = androidx.compose.ui.res.stringResource(modeName),
                style = MaterialTheme.typography.titleMedium,
            )
        }
    }
}

@Composable
private fun AdbPortCard(
    savedPort: String,
    enabled: Boolean,
    onSave: (String) -> Unit,
) {
    var port by remember(savedPort) { mutableStateOf(savedPort) }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.adb_port_title),
                style = MaterialTheme.typography.titleLarge,
            )
            Text(androidx.compose.ui.res.stringResource(R.string.adb_port_description))
            OutlinedTextField(
                value = port,
                onValueChange = { port = it.filter(Char::isDigit).take(5) },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.adb_connect_port)) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                enabled = enabled,
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = { onSave(port) },
                enabled = enabled && port.isNotBlank(),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(androidx.compose.ui.res.stringResource(R.string.save_adb_port))
            }
        }
    }
}

@Composable
private fun PairingRelayCard(
    pairingState: PairingRelayState,
    normalRelayRunning: Boolean,
    onStart: (String) -> Unit,
    onStop: () -> Unit,
) {
    var pairingPort by remember { mutableStateOf("") }
    val statusText = when (pairingState) {
        PairingRelayState.Off -> androidx.compose.ui.res.stringResource(R.string.pairing_status_off)
        PairingRelayState.Starting -> androidx.compose.ui.res.stringResource(R.string.pairing_status_starting)
        is PairingRelayState.Ready -> androidx.compose.ui.res.stringResource(
            R.string.pairing_status_ready,
            pairingState.pcLoopbackPort,
        )
        PairingRelayState.PcConnected -> androidx.compose.ui.res.stringResource(R.string.pairing_status_pc_connected)
        is PairingRelayState.Error -> pairingState.detail
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.pairing_title),
                style = MaterialTheme.typography.titleLarge,
            )
            Text(androidx.compose.ui.res.stringResource(R.string.pairing_description))
            Text(
                text = statusText,
                style = MaterialTheme.typography.labelLarge,
            )
            OutlinedTextField(
                value = pairingPort,
                onValueChange = { pairingPort = it.filter(Char::isDigit).take(5) },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.adb_pairing_port)) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                enabled = !pairingState.isRunning && !normalRelayRunning,
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = {
                    if (pairingState.isRunning) onStop() else onStart(pairingPort)
                },
                enabled = pairingState.isRunning || (!normalRelayRunning && pairingPort.isNotBlank()),
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(
                    androidx.compose.ui.res.stringResource(
                        if (pairingState.isRunning) R.string.stop_pairing_relay else R.string.start_pairing_relay,
                    ),
                )
            }
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.pairing_pc_step),
                style = MaterialTheme.typography.bodySmall,
            )
        }
    }
}

@Composable
private fun StatusCard(state: ConnectionState) {
    val status = when (state) {
        ConnectionState.SetupRequired -> R.string.status_setup_required
        ConnectionState.Off -> R.string.status_off
        ConnectionState.Starting -> R.string.status_starting
        ConnectionState.Ready -> R.string.status_ready
        is ConnectionState.PcConnected -> R.string.status_connected
        ConnectionState.Recovering -> R.string.status_recovering
        is ConnectionState.Error -> R.string.status_error
    }
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(20.dp)) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.status_title),
                style = MaterialTheme.typography.labelLarge,
            )
            Spacer(Modifier.height(8.dp))
            Text(
                text = androidx.compose.ui.res.stringResource(status),
                style = MaterialTheme.typography.headlineSmall,
            )
            if (state is ConnectionState.Error) {
                Spacer(Modifier.height(8.dp))
                Text(text = state.detail, style = MaterialTheme.typography.bodyMedium)
            }
        }
    }
}

@Composable
private fun ConnectionButton(
    state: ConnectionState,
    mainViewModel: MainViewModel,
    requestNotificationPermission: (String) -> Unit,
) {
    if (state is ConnectionState.SetupRequired) return
    Button(
        onClick = {
            if (state.isRunning) {
                mainViewModel.stop()
            } else {
                requestNotificationPermission(Manifest.permission.POST_NOTIFICATIONS)
            }
        },
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(
            androidx.compose.ui.res.stringResource(
                if (state.isRunning) R.string.stop_connection else R.string.start_connection,
            ),
        )
    }
}

@Composable
private fun RegistrationCard(
    mainViewModel: MainViewModel,
    registering: Boolean,
    connectionMode: ConnectionMode,
    registrationDraft: RegistrationDraft?,
    onScanQr: () -> Unit,
) {
    var host by remember { mutableStateOf("") }
    var port by remember { mutableStateOf("47156") }
    var fingerprint by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }

    LaunchedEffect(registrationDraft) {
        registrationDraft?.let { draft ->
            host = draft.host
            port = draft.port
            fingerprint = draft.fingerprint
            code = draft.registrationCode
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(20.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.setup_title),
                style = MaterialTheme.typography.titleLarge,
            )
            Text(
                androidx.compose.ui.res.stringResource(
                    if (connectionMode == ConnectionMode.EXISTING_VPN_ADB_ONLY) {
                        R.string.setup_description_existing_vpn
                    } else {
                        R.string.setup_description
                    },
                ),
            )
            if (connectionMode == ConnectionMode.EMBEDDED_SECURE_NETWORK) {
                WarningCard(androidx.compose.ui.res.stringResource(R.string.mode_embedded_unavailable))
            }
            Button(
                onClick = onScanQr,
                enabled = !registering && connectionMode == ConnectionMode.EXISTING_VPN_ADB_ONLY,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(androidx.compose.ui.res.stringResource(R.string.scan_registration_qr))
            }
            Text(
                text = androidx.compose.ui.res.stringResource(R.string.qr_manual_fallback),
                style = MaterialTheme.typography.bodySmall,
            )
            OutlinedTextField(
                value = host,
                onValueChange = { host = it },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.gateway_host)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = port,
                onValueChange = { port = it.filter(Char::isDigit).take(5) },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.gateway_port)) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = fingerprint,
                onValueChange = { fingerprint = it },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.gateway_fingerprint)) },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            OutlinedTextField(
                value = code,
                onValueChange = { code = it.filter(Char::isDigit).take(6) },
                label = { Text(androidx.compose.ui.res.stringResource(R.string.registration_code)) },
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Button(
                onClick = { mainViewModel.register(host, port, fingerprint, code) },
                enabled = !registering &&
                    connectionMode != ConnectionMode.EMBEDDED_SECURE_NETWORK &&
                    host.isNotBlank() &&
                    code.length == 6,
                modifier = Modifier.fillMaxWidth(),
            ) {
                Text(
                    androidx.compose.ui.res.stringResource(
                        if (registering) R.string.registration_in_progress else R.string.register_pc,
                    ),
                )
            }
        }
    }
}

@Composable
private fun WarningCard(message: String) {
    Card(
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.secondaryContainer,
        ),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(message, modifier = Modifier.padding(16.dp))
    }
}
