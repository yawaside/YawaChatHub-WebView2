import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Standalone-вывод: CI упаковывает .next/standalone в релизный архив
  // (веб-сборка YawaChatHub для самостоятельного деплоя).
  output: "standalone",
};

export default nextConfig;
