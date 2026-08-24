package io.github.fullmetalsonic.roamadb.runtime

import android.service.quicksettings.Tile
import android.service.quicksettings.TileService

class RoamAdbTileService : TileService() {
    override fun onStartListening() {
        super.onStartListening()
        RuntimeGraph.initialize(this)
        render()
    }

    override fun onClick() {
        super.onClick()
        RuntimeGraph.initialize(this)
        val shouldRun = !RuntimeGraph.connectionController.state.value.isRunning
        val action = {
            if (shouldRun) {
                RoamAdbService.start(this)
            } else {
                RoamAdbService.stop(this)
            }
            render(shouldRun)
        }
        if (isLocked) unlockAndRun(action) else action()
    }

    private fun render(running: Boolean = RuntimeGraph.connectionController.state.value.isRunning) {
        qsTile?.apply {
            state = if (running) Tile.STATE_ACTIVE else Tile.STATE_INACTIVE
            label = "RoamADB"
            subtitle = if (running) "Remote ADB on" else "Remote ADB off"
            updateTile()
        }
    }
}
