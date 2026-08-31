import type { Metadata, Viewport } from "next";
import type { ReactNode } from "react";
import { Inter, Unbounded } from "next/font/google";
import "./globals.css";

const inter = Inter({
  subsets: ["latin", "cyrillic"],
  variable: "--font-inter",
  display: "swap",
});

const unbounded = Unbounded({
  subsets: ["latin", "cyrillic"],
  variable: "--font-unbounded",
  weight: ["500", "600", "700", "800"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "YawaChatHub — все чаты стрима в одной ленте",
  description:
    "Единая лента сообщений Twitch, YouTube Live, VK Video Live, Kick и TikTok Live с озвучкой голосами Windows (SAPI5), виджетом для OBS и игровым оверлеем.",
};

export const viewport: Viewport = {
  themeColor: "#0b0e17",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="ru" data-theme="midnight" suppressHydrationWarning>
      <body className={`${inter.variable} ${unbounded.variable} antialiased`}>{children}</body>
    </html>
  );
}
