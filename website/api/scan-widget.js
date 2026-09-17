const { scanWidget } = require("./lib/widgetSecurity");

module.exports = async function handler(request, response) {
  response.setHeader("Cache-Control", "no-store");
  if (request.method !== "POST") return response.status(405).json({ error: "Method not allowed." });
  try {
    const report = await scanWidget(request.body?.code);
    return response.status(report.passed ? 200 : 422).json(report);
  } catch (error) {
    return response.status(503).json({ error: "The Ollama security scanner is unavailable. Publishing is blocked until it is back online.", detail: error.message });
  }
};
