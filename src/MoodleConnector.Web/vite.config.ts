import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
const nodeProcess = (globalThis as typeof globalThis & { process?: { env?: Record<string, string | undefined> } }).process;
const portalApiProxy = nodeProcess?.env?.PORTAL_API_PROXY ?? 'http://127.0.0.1:8787';
export default defineConfig({ base: '/portal/', plugins: [react()], server: { proxy: { '/api': portalApiProxy } } });
