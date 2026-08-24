package io.github.fullmetalsonic.roamadb.runtime

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.IBinder
import android.service.quicksettings.TileService
import androidx.core.app.NotificationCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.launch

class RoamAdbService : Service() {
    private val serviceScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private var startRequested = false
    private var preserveTerminalState = false

    override fun onCreate() {
        super.onCreate()
        RuntimeGraph.initialize(this)
        createNotificationChannel()
        serviceScope.launch {
            RuntimeGraph.connectionController.state.collectLatest { state ->
                requestTileRefresh()
                if (startRequested && !state.isRunning) {
                    stopAfterTerminalState()
                }
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                startRequested = false
                preserveTerminalState = false
                RuntimeGraph.connectionController.stop()
                stopForeground(STOP_FOREGROUND_REMOVE)
                stopSelf()
            }

            else -> {
                startForeground(NOTIFICATION_ID, buildNotification())
                startRequested = true
                preserveTerminalState = false
                RuntimeGraph.connectionController.start()
                if (!RuntimeGraph.connectionController.state.value.isRunning) {
                    stopAfterTerminalState()
                }
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        startRequested = false
        serviceScope.cancel()
        if (!preserveTerminalState) {
            RuntimeGraph.connectionController.stop()
        }
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun createNotificationChannel() {
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(
                CHANNEL_ID,
                "RoamADB connection",
                NotificationManager.IMPORTANCE_LOW,
            ).apply {
                description = "Shows when the user-requested remote ADB connection is active."
            },
        )
    }

    private fun buildNotification() = NotificationCompat.Builder(this, CHANNEL_ID)
        .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
        .setContentTitle("RoamADB")
        .setContentText("Remote debugging connection is starting")
        .setOngoing(true)
        .addAction(
            0,
            "Stop",
            PendingIntent.getService(
                this,
                1,
                Intent(this, RoamAdbService::class.java).setAction(ACTION_STOP),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            ),
        )
        .build()

    private fun requestTileRefresh() {
        TileService.requestListeningState(
            this,
            ComponentName(this, RoamAdbTileService::class.java),
        )
    }

    private fun stopAfterTerminalState() {
        if (!startRequested) return
        startRequested = false
        preserveTerminalState = true
        stopForeground(STOP_FOREGROUND_REMOVE)
        requestTileRefresh()
        stopSelf()
    }

    companion object {
        private const val CHANNEL_ID = "roamadb-connection"
        private const val NOTIFICATION_ID = 4701
        private const val ACTION_START = "io.github.fullmetalsonic.roamadb.START"
        private const val ACTION_STOP = "io.github.fullmetalsonic.roamadb.STOP"

        fun start(context: Context) {
            context.startForegroundService(
                Intent(context, RoamAdbService::class.java).setAction(ACTION_START),
            )
        }

        fun stop(context: Context) {
            context.startService(
                Intent(context, RoamAdbService::class.java).setAction(ACTION_STOP),
            )
        }
    }
}
