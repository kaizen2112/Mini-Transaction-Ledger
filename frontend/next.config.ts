import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Traces the actually-used dependencies into a self-contained server.js,
  // so the Docker runtime image (frontend/Dockerfile) doesn't have to carry
  // the full node_modules tree. docs/08-docker.md §4.
  output: "standalone",
};

export default nextConfig;
