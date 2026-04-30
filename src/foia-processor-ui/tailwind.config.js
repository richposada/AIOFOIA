/** @type {import('tailwindcss').Config} */
export default {
    content: ["./index.html", "./src/**/*.{ts,tsx}"],
    theme: {
        extend: {
            colors: {
                midnight: {
                    50: "#f3f5fb",
                    100: "#e2e8f3",
                    200: "#c1cce4",
                    300: "#94a6cf",
                    400: "#6680b6",
                    500: "#46619c",
                    600: "#364c7c",
                    700: "#2c3d62",
                    800: "#212d49",
                    900: "#161e33",
                    950: "#0b1020",
                },
            },
            boxShadow: {
                card: "0 1px 2px rgba(0,0,0,0.25), 0 8px 24px -12px rgba(0,0,0,0.45)",
            },
            keyframes: {
                "status-pulse": {
                    "0%, 100%": {
                        backgroundColor: "rgba(16, 185, 129, 0.15)",
                        boxShadow: "0 0 0 0 rgba(16, 185, 129, 0.0)",
                    },
                    "50%": {
                        backgroundColor: "rgba(16, 185, 129, 0.45)",
                        boxShadow: "0 0 12px 2px rgba(16, 185, 129, 0.55)",
                    },
                },
                "status-dot": {
                    "0%, 100%": { opacity: "0.4", transform: "scale(0.85)" },
                    "50%": { opacity: "1", transform: "scale(1.15)" },
                },
            },
            animation: {
                "status-pulse": "status-pulse 1.8s ease-in-out infinite",
                "status-dot": "status-dot 1.8s ease-in-out infinite",
            },
        },
    },
    plugins: [],
};
