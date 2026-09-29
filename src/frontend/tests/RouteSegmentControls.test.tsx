import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import RouteSegmentControls from '../src/RouteSegmentControls';
import type { RouteSegment } from '../src/types';

const segments: RouteSegment[] = [
  { fromPointIndex: 0, toPointIndex: 1, surface: 'asphalt', wayType: 'cycleway' },
  { fromPointIndex: 1, toPointIndex: 2, surface: 'unknown', wayType: 'footway' },
];
const props = { locale: 'en' as const, mode: 'surface' as const, onModeChange: vi.fn(), segments, status: 'valid' as const,
  selectedSegmentIndex: null, onSegmentSelect: vi.fn(), color: '#15724f' };
it('supports keyboard mode selection and exact segment inspection', async () => {
  const onModeChange = vi.fn(); const onSegmentSelect = vi.fn();
  const ui = render(<RouteSegmentControls {...props} onModeChange={onModeChange} onSegmentSelect={onSegmentSelect} />);
  expect(screen.getByRole('radio', { name: 'Surface' })).toBeChecked();
  screen.getByRole('radio', { name: 'Road type' }).focus();
  await userEvent.keyboard(' ');
  expect(onModeChange).toHaveBeenCalledWith('wayType');
  await userEvent.click(screen.getByText('Track segments'));
  await userEvent.selectOptions(screen.getByLabelText('Segment'), '1');
  expect(onSegmentSelect).toHaveBeenCalledWith(1);
  ui.rerender(<RouteSegmentControls {...props} selectedSegmentIndex={1} />);
  expect(screen.getByRole('region', { name: 'Segment details' })).toHaveTextContent('Unknown surface');
  expect(screen.getByRole('region', { name: 'Segment details' })).toHaveTextContent('Footway');
});
it('marks malformed metadata unavailable and handles an empty result', () => {
  const ui = render(<RouteSegmentControls {...props} status="invalid" />);
  expect(screen.getByRole('status')).toHaveTextContent('Segment data is unavailable');
  ui.rerender(<RouteSegmentControls {...props} segments={[]} />);
  expect(screen.queryByRole('radio')).not.toBeInTheDocument();
});
it.each(['ru', 'he'] as const)('renders localized controls (%s)', locale => {
  render(<RouteSegmentControls {...props} locale={locale} />);
  expect(screen.getAllByRole('radio')).toHaveLength(2);
  expect(screen.queryByRole('radio', { name: 'Surface' })).not.toBeInTheDocument();
});
