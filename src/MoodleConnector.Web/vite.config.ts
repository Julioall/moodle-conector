import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
export default defineConfig({ base: '/portal/', plugins: [react()], server: { proxy: { '/api': 'http://localhost:5000' } } });
