package io.github.fullmetalsonic.roamadb

import android.app.Application
import io.github.fullmetalsonic.roamadb.runtime.RuntimeGraph

class RoamAdbApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        RuntimeGraph.initialize(this)
    }
}
