package io.github.fullmetalsonic.roamadb.model

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class GatewayProfileTest {
    @Test
    fun `fingerprint separators are normalized`() {
        val raw = List(32) { "ab" }.joinToString(":")
        assertEquals("AB".repeat(32), GatewayProfile.normalizeFingerprint(raw))
    }

    @Test
    fun `malformed fingerprint is rejected`() {
        assertFailsWith<IllegalArgumentException> {
            GatewayProfile.normalizeFingerprint("not-a-fingerprint")
        }
    }

    @Test
    fun `running state is explicit`() {
        assertFalse(ConnectionState.Off.isRunning)
        assertTrue(ConnectionState.Starting.isRunning)
        assertTrue(ConnectionState.PcConnected("Home Gateway").isRunning)
        assertFalse(ConnectionState.Error("test", "test").isRunning)
    }

    @Test
    fun `wireless adb connect port is validated`() {
        assertEquals(37123, LocalAdbEndpoint(37123).connectPort)
        assertFailsWith<IllegalArgumentException> { LocalAdbEndpoint(0) }
        assertFailsWith<IllegalArgumentException> { LocalAdbEndpoint(65536) }
    }

    @Test
    fun `connection mode storage value is strict`() {
        assertEquals(
            ConnectionMode.EXISTING_VPN_ADB_ONLY,
            ConnectionMode.fromStorage("EXISTING_VPN_ADB_ONLY"),
        )
        assertEquals(null, ConnectionMode.fromStorage("UNKNOWN_FUTURE_MODE"))
    }
}
