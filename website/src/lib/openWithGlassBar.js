export function openWithGlassBar(item) {
  const packageUrl = new URL("/api/design-package", window.location.origin);
  packageUrl.searchParams.set("id", item.id);
  window.location.href = `glassbar://open?url=${encodeURIComponent(packageUrl.toString())}`;
}
