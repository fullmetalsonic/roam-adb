package io.github.fullmetalsonic.roamadb.security

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature
import java.security.spec.ECGenParameterSpec

class PhoneIdentityStore(
    private val alias: String = "roamadb-phone-identity-v1",
) {
    private val keyStore: KeyStore
        get() = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    fun publicKeySpkiBase64(): String {
        ensureKey()
        val publicKey = keyStore.getCertificate(alias).publicKey.encoded
        return android.util.Base64.encodeToString(publicKey, android.util.Base64.NO_WRAP)
    }

    fun sign(challenge: ByteArray): ByteArray {
        ensureKey()
        val privateKey = keyStore.getKey(alias, null)
        return Signature.getInstance("SHA256withECDSA").run {
            initSign(privateKey as java.security.PrivateKey)
            update(challenge)
            sign()
        }
    }

    fun reset() {
        keyStore.deleteEntry(alias)
    }

    private fun ensureKey() {
        if (keyStore.containsAlias(alias)) return

        val generator = KeyPairGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_EC,
            "AndroidKeyStore",
        )
        val specification = KeyGenParameterSpec.Builder(
            alias,
            KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY,
        )
            .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .setUserAuthenticationRequired(false)
            .setInvalidatedByBiometricEnrollment(false)
            .build()
        generator.initialize(specification)
        generator.generateKeyPair()
    }
}
