package com.sarmat.crew

import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BatteryDisplayStateTest {
    private fun battery(id: String, last: String? = null, active: String? = null) = com.sarmat.crew.api.BatterySummary(
        id, id, id, "Test", 10.0, 36.0, 50.4, 12, "ready", "li-ion", 0,
        active, null, null, null, null, null, null, lastActiveSince = last,
    )

    @Test fun `last drone battery uses installation time and survives removal`() {
        val first = battery("1", "2026-10-08T10:00:00Z")
        val second = battery("2", "2026-10-08T11:00:00Z")
        assertEquals("2", lastDroneBatteryId(listOf(second, first)))
        assertEquals(null, lastDroneBatteryId(listOf(battery("3"))))
        assertEquals("1", lastDroneBatteryId(listOf(first.copy(activeSince = first.lastActiveSince), second)))
    }

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
