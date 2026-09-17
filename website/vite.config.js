const { defineConfig } = require("vite");
const react = require("@vitejs/plugin-react");

module.exports = defineConfig({
  plugins: [react()],
  build: {
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes("@supabase")) return "studio-services";
          if (id.includes("node_modules/react")) return "react-vendor";
        }
      }
    }
  }
});
