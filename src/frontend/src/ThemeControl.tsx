import { Monitor, Moon, Sun } from 'lucide-react';
import { useTheme } from 'next-themes';
import { t } from './i18n';
import type { Locale } from './types';

export default function ThemeControl({ locale }: { locale: Locale }) {
  const { theme, setTheme } = useTheme();
  const value = theme === 'light' || theme === 'dark' ? theme : 'system';
  const Icon = value === 'system' ? Monitor : value === 'dark' ? Moon : Sun;
  return <label className="theme-control">
    <Icon size={16} aria-hidden="true" />
    <span className="sr-only">{t(locale, 'theme')}</span>
    <select value={value} onChange={event => setTheme(event.target.value)}>
      <option value="system">{t(locale, 'themeSystem')}</option>
      <option value="light">{t(locale, 'themeLight')}</option>
      <option value="dark">{t(locale, 'themeDark')}</option>
    </select>
  </label>;
}
