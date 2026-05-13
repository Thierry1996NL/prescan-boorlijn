/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        blauw: "#1B6EF3",
        donker: "#0f1117",
        panel: "#141824",
        rand: "#1e2433",
      },
    },
  },
  plugins: [],
};
