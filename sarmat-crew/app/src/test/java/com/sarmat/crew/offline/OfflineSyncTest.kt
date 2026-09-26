package com.sarmat.crew.offline

import android.content.Context
import com.sarmat.crew.api.CrewApi
import okhttp3.mockwebserver.Dispatcher
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.RecordedRequest
import org.json.JSONArray
import org.json.JSONObject
import org.junit.After
import org.junit.Before
import org.junit.Test
import org.junit.Assert.*
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import java.util.UUID

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [28])
class OfflineSyncTest {
    private lateinit var context: Context
    private lateinit var server: MockWebServer
    private lateinit var store: OfflineStore
    private lateinit var repo: CrewRepository
    private lateinit var remote: CrewApi
    private lateinit var scope: String
    private val user = UUID.randomUUID().toString()
    private val crew = UUID.randomUUID().toString()
    private val battery = UUID.randomUUID().toString()
    private val received = linkedMapOf<String, JSONObject>()
    private var failAfterCommit = false
    private var failSnapshot = false
    private var rejectStatus = false
    private var expired = false
    private var offline = false
    private var activeSince: String? = null

    @Before fun setup() {
        context = RuntimeEnvironment.getApplication()
        store = OfflineStore(context)
        server = MockWebServer()
        server.dispatcher = object : Dispatcher() {
          override fun dispatch(request: RecordedRequest): MockResponse {
            val path = request.requestUrl!!.encodedPath
            var status = 200
            val response: Any = when {
                offline -> { status = 503; JSONObject().put("error", "offline") }
                expired -> { status = 401; JSONObject().put("error", "expired") }
                path == "/api/auth/me" -> JSONObject().put("id", user).put("crewId", crew).put("role", "CREW")
                path == "/api/crew/sync-capabilities" -> JSONObject().put("version", 1)
                path == "/api/crew/sync" -> {
                    val body = JSONObject(request.body.readUtf8())
                    if (rejectStatus && body.getString("kind") == "active") {
                        status = 409; JSONObject().put("error", "competing status")
                    } else {
                        received.putIfAbsent(body.getString("id"), body)
                        if (body.getString("kind") == "active") activeSince = if (body.getBoolean("active")) body.getString("occurredAt") else null
                        if (failAfterCommit) { status = 503; failAfterCommit = false; JSONObject().put("error", "lost acknowledgement") }
                        else JSONObject().put("id", body.getString("id"))
                    }
                }
                path == "/api/batteries" -> if (failSnapshot) { status = 503; JSONObject().put("error", "download failed") } else JSONArray().put(batteryJson())
                path == "/api/settings/thresholds" -> JSONObject().put("warningCellDeltaV", .1).put("dangerCellDeltaV", .2)
                path.endsWith("/history") -> JSONObject().put("items", JSONArray()).put("nextOffset", JSONObject.NULL)
                else -> { status = 404; JSONObject().put("error", path) }
            }
            return MockResponse().setResponseCode(status).setBody(response.toString())
          }
        }
        server.start()
        val url = server.url("/").toString().trimEnd('/')
        scope = "$url|$user|$crew"
        val preferences = context.getSharedPreferences(UUID.randomUUID().toString(), Context.MODE_PRIVATE)
        preferences.edit().putString("server_url", url).putString("session_cookie", "test=1")
            .putString("offline_scope", scope).putString("offline_crew_id", crew).commit()
        remote = CrewApi(preferences)
        repo = CrewRepository(context, remote, store) { }
        assertTrue(repo.sync())
    }
    @After fun cleanup() { server.shutdown(); store.close() }

    private fun batteryJson() = JSONObject().put("id", battery).put("label", "1").put("serialNumber", "test")
        .put("typeName", "12S").put("capacityAh", 20).put("minVoltage", 36).put("maxVoltage", 50.4)
        .put("cellCount", 12).put("state", "ready").put("chemistry", "Li-ion").put("cycleCount", 0)
        .put("activeSince", activeSince ?: JSONObject.NULL)
    private fun save() { repo.saveMeasurement(battery, List(12) { 4.24 }, "offline reading") }

    @Test fun `offline edits drafts and history survive reopening sqlite`() {
        offline = true
        val lastSync = repo.lastSync()
        repo.saveDraft(battery, listOf("4.10", ""), "draft", 0, null)
        save()
        repo.toggleActive(battery)
        repo.saveDraft(battery, listOf("4.20", ""), "next draft", 0, null)
        assertFalse(repo.sync())
        assertEquals(lastSync, repo.lastSync())
        store.close(); store = OfflineStore(context)
        repo = CrewRepository(context, remote, store) { }
        assertEquals(2, repo.pendingCount())
        assertEquals("next draft", repo.draft(battery)?.getString("notes"))
        assertEquals(4.24, repo.batteries().single().latestCells!!.first(), .0001)
        assertNotNull(repo.batteries().single().activeSince)
        assertEquals(1, repo.batteryHistory(battery, 0).items.size)
    }

    @Test fun `lost acknowledgement replays the same immutable operation`() {
        save(); val id = repo.pending().single().id
        failAfterCommit = true
        assertFalse(repo.sync())
        assertEquals(id, repo.pending().single().id)
        assertEquals(1, received.size)
        assertTrue(repo.sync())
        assertEquals(1, received.size)
        assertEquals(0, repo.pendingCount())
    }

    @Test fun `acknowledged data stays local until snapshot download commits`() {
        save(); failSnapshot = true
        assertFalse(repo.sync())
        assertEquals("sent", repo.pending().single().state)
        assertEquals(4.24, repo.batteries().single().latestCells!!.first(), .0001)
        failSnapshot = false
        assertTrue(repo.sync())
        assertEquals(1, received.size)
        assertEquals(0, repo.pendingCount())
    }

    @Test fun `status conflict keeps local intent but does not block independent measurement`() {
        repo.toggleActive(battery); repo.toggleActive(battery); save()
        rejectStatus = true
        assertTrue(repo.sync())
        assertEquals(1, received.size)
        assertEquals(2, repo.pendingCount())
        assertEquals("blocked", repo.pending().first().state)
        repo.acceptServerActiveState()
        assertEquals(0, repo.pendingCount())
        assertNull(repo.batteries().single().activeSince)
    }

    @Test fun `expired session preserves offline access and unsent changes`() {
        save(); expired = true
        assertTrue(repo.sync())
        assertTrue(repo.needsLogin)
        assertTrue(repo.hasSession())
        assertEquals(1, repo.pendingCount())
        assertEquals(1, repo.batteries().size)
    }

    @Test fun `different account scope cannot see or upload the previous queue`() {
        save()
        val other = context.getSharedPreferences("other-${UUID.randomUUID()}", Context.MODE_PRIVATE)
        other.edit().putString("server_url", remote.baseUrl).putString("offline_scope", "another-account").commit()
        val another = CrewRepository(context, CrewApi(other), store) { }
        assertEquals(0, another.pendingCount())
        assertTrue(another.batteries().isEmpty())
        assertEquals(1, repo.pendingCount())
    }
}
