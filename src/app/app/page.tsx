import type { Metadata } from "next";
import DesktopApp from "@/components/desktop-app";

export const metadata: Metadata = {
  title: "YawaChatHub — приложение",
};

export default function AppPage() {
  return (
    <div className="h-screen w-screen overflow-hidden">
      <DesktopApp />
    </div>
  );
}
