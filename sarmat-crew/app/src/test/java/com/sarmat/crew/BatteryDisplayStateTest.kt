package com.sarmat.crew

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryDisplayStateTest {
    @Test fun `configured discharge threshold is inclusive`() {
        assertTrue(isBatteryDischarged("ready", 50, 50))
        assertFalse(isBatteryDischarged("ready", 51, 50))
    }

    @Test fun `explicit operational states are preserved`() {
        assertFalse(isBatteryDischarged("charging", 20, 50))
        assertFalse(isBatteryDischarged("service", 20, 50))
        assertFalse(isBatteryDischarged("ready", null, 50))
    }
}
