package io.github.fullmetalsonic.roamadb.model

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class RegistrationQrPayloadTest {
    private val fingerprint = "A1".repeat(32)

    @Test
    fun parsesValidGatewayPayload() {
        val value = "roamadb://register?v=1&host=100.95.12.3&port=47156" +
            "&fingerprint=$fingerprint&code=123456&mode=existing-vpn-adb-only&expires=1120"
        val parsed = RegistrationQrPayload.parse(value, nowEpochSeconds = 1_000)
        assertEquals("100.95.12.3", parsed.host)
        assertEquals(47_156, parsed.port)
        assertEquals("123456", parsed.registrationCode)
    }

    @Test
    fun rejectsExpiredPayload() {
        val value = "roamadb://register?v=1&host=100.95.12.3&port=47156" +
            "&fingerprint=$fingerprint&code=123456&mode=existing-vpn-adb-only&expires=999"
        assertFailsWith<IllegalArgumentException> {
            RegistrationQrPayload.parse(value, nowEpochSeconds = 1_000)
        }
    }

    @Test
    fun rejectsNonTailnetAndDuplicateFields() {
        val publicHost = "roamadb://register?v=1&host=8.8.8.8&port=47156" +
            "&fingerprint=$fingerprint&code=123456&mode=existing-vpn-adb-only&expires=1120"
        assertFailsWith<IllegalArgumentException> {
            RegistrationQrPayload.parse(publicHost, nowEpochSeconds = 1_000)
        }

        val duplicate = "roamadb://register?v=1&v=1&host=100.95.12.3&port=47156" +
            "&fingerprint=$fingerprint&code=123456&mode=existing-vpn-adb-only&expires=1120"
        assertFailsWith<IllegalArgumentException> {
            RegistrationQrPayload.parse(duplicate, nowEpochSeconds = 1_000)
        }
    }
}
