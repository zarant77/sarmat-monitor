package com.sarmat.crew

import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
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

    @Test fun `widget uses configured discharged and critical boundaries`() {
        assertEquals(BatteryWidgetLevel.FULL, batteryWidgetLevel(71, 70, 20))
        assertEquals(BatteryWidgetLevel.LOW, batteryWidgetLevel(70, 70, 20))
        assertEquals(BatteryWidgetLevel.LOW, batteryWidgetLevel(20, 70, 20))
        assertEquals(BatteryWidgetLevel.CRITICAL, batteryWidgetLevel(19, 70, 20))
        assertEquals(BatteryWidgetLevel.UNKNOWN, batteryWidgetLevel(null, 70, 20))
    }
}
