import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ThemeProvider } from 'next-themes';
import App from './App';
import 'maplibre-gl/dist/maplibre-gl.css';
import './styles.css';

createRoot(document.getElementById('root')!).render(<StrictMode><ThemeProvider storageKey="cycling-routes-theme" defaultTheme="system" enableSystem disableTransitionOnChange><App /></ThemeProvider></StrictMode>);
