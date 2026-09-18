import test from "node:test";
import assert from "node:assert/strict";
import publishDesign from "../api/publish-design.mjs";
import moderation from "../api/moderation.mjs";

process.env.SUPABASE_URL = "https://studio.test";
process.env.SUPABASE_PUBLISHABLE_KEY = "\uFEFFanon-key\r\n";
process.env.SUPABASE_SERVICE_ROLE_KEY = "\uFEFFservice-key\r\n";
process.env.STUDIO_ADMIN_EMAIL = "\uFEFFethanhuynh365@gmail.com\r\n";

function token(aal = "aal2") {
  return `header.${Buffer.from(JSON.stringify({ aal })).toString("base64url")}.signature`;
}

function request(method, body) {
  return { method, body, headers: { authorization: `Bearer ${token()}` } };
}

function response() {
  return {
    statusCode: 200,
    payload: null,
    setHeader() {},
    status(value) { this.statusCode = value; return this; },
    json(value) { this.payload = value; return this; }
  };
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
}

test("coded widgets enter the pending queue without an automated scanner", async () => {
  const calls = [];
  const originalFetch = global.fetch;
  global.fetch = async (url, options = {}) => {
    calls.push({ url: String(url), options });
    if (String(url).endsWith("/auth/v1/user")) return json({
      id: "11111111-1111-4111-8111-111111111111",
      email: "creator@example.com",
      user_metadata: { username: "creator" }
    });
    if (String(url).includes("/rest/v1/profiles")) return new Response(null, { status: 201 });
    if (String(url).includes("/rest/v1/designs")) return json([{ id: "22222222-2222-4222-8222-222222222222" }], 201);
    throw new Error(`Unexpected request: ${url}`);
  };

  try {
    const result = response();
    await publishDesign(request("POST", {
      username: "creator",
      document: {
        schemaVersion: 1,
        kind: "widget",
        metadata: { name: "Focus Timer", summary: "Manual review sample", tags: ["focus"] },
        widget: { mode: "code", code: { html: "<div>Focus</div>", css: "div { color: white; }", js: "document.querySelector('div').textContent = 'Ready';" } }
      }
    }), result);

    assert.equal(result.statusCode, 201);
    assert.equal(result.payload.status, "pending");
    assert.deepEqual(Object.keys(result.payload).sort(), ["id", "status"]);
    assert.equal(calls.some((call) => call.url.includes("11434") || call.url.includes("api/chat")), false);
    const insert = calls.find((call) => call.url.includes("/rest/v1/designs"));
    assert.equal(JSON.parse(insert.options.body).status, "pending");
  } finally {
    global.fetch = originalFetch;
  }
});

test("the moderation API rejects every account except the configured reviewer", async () => {
  const originalFetch = global.fetch;
  global.fetch = async (url) => String(url).endsWith("/auth/v1/user")
    ? json({ id: "11111111-1111-4111-8111-111111111111", email: "creator@example.com" })
    : (() => { throw new Error(`Unexpected request: ${url}`); })();

  try {
    const result = response();
    await moderation(request("GET"), result);
    assert.equal(result.statusCode, 403);
    assert.equal(result.payload.error, "This account cannot moderate designs.");
  } finally {
    global.fetch = originalFetch;
  }
});

test("the configured reviewer can load pending designs", async () => {
  const originalFetch = global.fetch;
  global.fetch = async (url) => {
    if (String(url).endsWith("/auth/v1/user")) return json({
      id: "33333333-3333-4333-8333-333333333333",
      email: "ethanhuynh365@gmail.com"
    });
    if (String(url).includes("status=eq.pending")) return json([{ id: "pending-design", name: "Waiting" }]);
    throw new Error(`Unexpected request: ${url}`);
  };

  try {
    const result = response();
    await moderation(request("GET"), result);
    assert.equal(result.statusCode, 200);
    assert.equal(result.payload.designs[0].name, "Waiting");
  } finally {
    global.fetch = originalFetch;
  }
});
