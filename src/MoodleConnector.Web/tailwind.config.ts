import type { Config } from 'tailwindcss';
export default { content: ['./index.html', './src/**/*.{ts,tsx}'], theme: { extend: { colors: { primary: '#3f51b5' } } }, plugins: [] } satisfies Config;
