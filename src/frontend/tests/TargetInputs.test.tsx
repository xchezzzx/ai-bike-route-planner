import { useState } from 'react';
import { expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import TargetInputs from '../src/TargetInputs';
import { buildManual } from '../src/request';
import { t } from '../src/i18n';
import type { Locale, ManualInput } from '../src/types';

const initial: ManualInput = { shape: 'loop', profile: 'road', elevation: 'balanced', distance: '25', duration: '' };
const start = { latitude: 32.08, longitude: 34.78 };
let current: ManualInput;

function Form({ value = initial, locale = 'en' }: { value?: ManualInput; locale?: Locale }) {
  const [input, setInput] = useState(value);
  current = input;
  return <TargetInputs locale={locale} value={input} onChange={setInput} />;
}

it('replaces range fields with two keyboard-controlled thumbs and submits both bounds', async () => {
  const user = userEvent.setup();
  render(<Form value={{ ...initial, distanceMode: 'range', distanceMin: '35', distanceMax: '45' }} />);
  const group = within(screen.getByRole('group', { name: 'Distance (km)' }));
  expect(group.queryAllByRole('textbox')).toHaveLength(0);
  const minimum = group.getByRole('slider', { name: 'Minimum' });
  const maximum = group.getByRole('slider', { name: 'Maximum' });
  minimum.focus();
  await user.keyboard('{ArrowRight}');
  maximum.focus();
  await user.keyboard('{ArrowRight}');
  expect(minimum).toHaveAttribute('aria-valuenow', '36');
  expect(maximum).toHaveAttribute('aria-valuenow', '46');
  expect(minimum).toHaveAttribute('aria-valuetext', '36 km');
  expect(buildManual(current, start)).toMatchObject({ targetDistanceRangeMeters: { min: 36000, max: 46000 } });
  expect(buildManual(current, start)).not.toHaveProperty('targetDistanceMeters');
});

it('initializes a usable range and retains its draft across target/range switches', async () => {
  const user = userEvent.setup();
  render(<Form />);
  const group = within(screen.getByRole('group', { name: 'Distance (km)' }));
  await user.click(group.getByRole('radio', { name: 'Range' }));
  expect(group.getByRole('slider', { name: 'Minimum' })).toHaveAttribute('aria-valuenow', '35');
  expect(group.getByRole('slider', { name: 'Maximum' })).toHaveAttribute('aria-valuenow', '45');
  group.getByRole('slider', { name: 'Maximum' }).focus();
  await user.keyboard('{ArrowUp}');
  await user.click(group.getByRole('radio', { name: 'Target' }));
  expect(group.getByRole('textbox')).toHaveValue('25');
  expect(buildManual(current, start)).not.toHaveProperty('targetDistanceRangeMeters');
  await user.click(group.getByRole('radio', { name: 'Range' }));
  expect(group.getByRole('slider', { name: 'Maximum' })).toHaveAttribute('aria-valuenow', '46');
});

it('leaves duration optional and can clear and reactivate its range', async () => {
  const user = userEvent.setup();
  render(<Form />);
  expect(buildManual(current, start)).not.toHaveProperty('targetDurationRangeSeconds');
  const group = within(screen.getByRole('group', { name: 'Duration (min)' }));
  await user.click(group.getByRole('radio', { name: 'Range' }));
  expect(buildManual(current, start)).toMatchObject({ targetDurationRangeSeconds: { min: 3600, max: 7200 } });
  await user.click(group.getByRole('button', { name: 'Clear range' }));
  expect(buildManual(current, start)).not.toHaveProperty('targetDurationRangeSeconds');
  expect(group.getByRole('slider', { name: 'Minimum' })).toHaveAttribute('aria-disabled', 'true');
  expect(group.getByText('Not specified')).toBeInTheDocument();
  await user.click(group.getByRole('button', { name: 'Set range' }));
  expect(group.getByRole('slider', { name: 'Minimum' })).not.toHaveAttribute('aria-disabled', 'true');
  expect(buildManual(current, start)).toMatchObject({ targetDurationRangeSeconds: { min: 3600, max: 7200 } });
});

it('preserves existing decimal and extended bounds without silently clipping them', () => {
  render(<Form value={{ ...initial, distanceMode: 'range', distanceMin: '0.5', distanceMax: '150.5' }} />);
  const group = within(screen.getByRole('group', { name: 'Distance (km)' }));
  expect(group.getByRole('slider', { name: 'Minimum' })).toHaveAttribute('aria-valuenow', '0.5');
  expect(group.getByRole('slider', { name: 'Maximum' })).toHaveAttribute('aria-valuenow', '150.5');
  expect(buildManual(current, start)).toMatchObject({ targetDistanceRangeMeters: { min: 500, max: 150500 } });
});

it.each(['en', 'ru', 'he'] as const)('keeps ordered bounds during keyboard edits in %s', async locale => {
  const user = userEvent.setup();
  render(<Form locale={locale} value={{ ...initial, distanceMode: 'range', distanceMin: '35', distanceMax: '45' }} />);
  const group = within(screen.getByRole('group', { name: t(locale, 'distanceInput') }));
  const minimum = group.getByRole('slider', { name: t(locale, 'minimum') });
  minimum.focus();
  await user.keyboard(locale === 'he' ? '{ArrowLeft}' : '{ArrowRight}');
  expect(minimum).toHaveAttribute('aria-valuenow', '36');
  await user.keyboard('{End}{ArrowUp}');
  const range = buildManual(current, start).targetDistanceRangeMeters!;
  expect(range.min).toBeLessThanOrEqual(range.max);
  expect(range.min).toBeGreaterThan(0);
  expect(range.max).toBeLessThanOrEqual(100000);
});
