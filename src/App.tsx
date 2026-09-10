import { useEffect, useState } from "react";
import DesktopApp from "./components/DesktopApp";
import OverlayApp from "./components/OverlayApp";
import WidgetApp from "./components/WidgetApp";

function useHashRoute(): string {
  const [hash, setHash] = useState(() => window.location.hash);
  useEffect(() => {
    const onChange = () => setHash(window.location.hash);
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);
  const path = hash.replace(/^#/, "").split("?")[0];
  return path || "/app";
}

export default function App() {
  const route = useHashRoute();
  if (route === "/overlay") return <OverlayApp />;
  if (route === "/widget") return <WidgetApp />;
  // по умолчанию — окно приложения: «демо = настоящее окно программы»
  return <DesktopApp />;
}
