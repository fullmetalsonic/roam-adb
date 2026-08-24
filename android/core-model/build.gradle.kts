import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import java.io.File

plugins {
    kotlin("jvm")
}

kotlin {
    jvmToolchain(21)
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
    }
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

// Gradle's Windows test worker can misread non-ASCII classpath entries from its
// argument file. Keep sources in place and relocate only this JVM module's build
// output to the nearest ASCII-only ancestor when necessary.
val projectPath = rootProject.rootDir.absolutePath
if (projectPath.any { it.code > 127 }) {
    var asciiAncestor: File = rootProject.rootDir
    while (asciiAncestor.absolutePath.any { it.code > 127 } && asciiAncestor.parentFile != null) {
        asciiAncestor = asciiAncestor.parentFile
    }
    val pathKey = Integer.toUnsignedString(projectPath.lowercase().hashCode(), 16)
    layout.buildDirectory.set(File(asciiAncestor, ".roamadb-build/$pathKey/core-model"))
}

dependencies {
    testImplementation(kotlin("test"))
}

tasks.test {
    useJUnitPlatform()
}
