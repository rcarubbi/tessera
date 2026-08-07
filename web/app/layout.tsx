import type { Metadata } from "next";
import { AuthProvider } from "@/components/AuthContext";
import PrelineClient from "@/components/PrelineClient";
import "../theme.css";

export const metadata: Metadata = {
  title: "Tessera — Architecture Dashboard",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <AuthProvider>{children}</AuthProvider>
        <PrelineClient />
      </body>
    </html>
  );
}
