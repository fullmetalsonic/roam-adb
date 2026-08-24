plugins {
    id("com.android.library")
    kotlin("android")
}

android {
    namespace = "io.github.fullmetalsonic.roamadb.connection"
    compileSdk = 35

    defaultConfig {
        minSdk = 35
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation(project(":core-model"))
    implementation(project(":security"))
    implementation(project(":direct-tunnel"))
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")
}
