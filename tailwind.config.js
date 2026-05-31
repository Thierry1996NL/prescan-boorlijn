/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./app/**/*.{js,ts,jsx,tsx,mdx}",
    "./components/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        // Borevexa brand tokens (light mode)
        bv: {
          green:     "#007A5A",
          "green-h": "#00915F",
          "green-l": "#E5F3EC",
          "green-b": "rgba(0,122,90,0.18)",
          dark:      "#0D1520",
          text:      "#1B2B35",
          muted:     "#587080",
          soft:      "#8FA6B2",
          border:    "#DEE6EA",
          bg:        "#F5F7F9",
          bg2:       "#EEF2F4",
        },
      },
      fontFamily: {
        sans: ["Inter", "system-ui", "sans-serif"],
      },
    },
  },
  plugins: [],
};
