import "./globals.css";

export const metadata = {
  title: "Borevexa — HDD Prescan Platform",
  description: "Haalbaarheidsanalyse voor horizontaal gestuurd boren",
};

export default function RootLayout({ children }) {
  return (
    <html lang="nl">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"
          rel="stylesheet"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
