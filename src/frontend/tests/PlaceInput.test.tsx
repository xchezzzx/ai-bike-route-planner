import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { expect, it, vi } from 'vitest';
import PlaceInput from '../src/PlaceInput';

it('scrolls the active keyboard suggestion into view', () => {
  const scroll = vi.fn();
  const original = HTMLElement.prototype.scrollIntoView;
  HTMLElement.prototype.scrollIntoView = scroll;
  try {
    render(<PlaceInput field="start" locale="en" onChange={vi.fn()} onSelect={vi.fn()} />);
    const input = screen.getByRole('combobox');
    fireEvent.change(input, { target: { value: 'ha' } });
    const options = screen.getAllByRole('option');
    expect(options).toHaveLength(8);
    fireEvent.keyDown(input, { key: 'ArrowUp' });
    expect(options[7]).toHaveAttribute('aria-selected', 'true');
    expect(scroll).toHaveBeenCalledWith({ block: 'nearest' });
    expect(scroll.mock.instances.at(-1)).toBe(options[7]);
  } finally { HTMLElement.prototype.scrollIntoView = original; }
});

it('requires selection and supports keyboard settlement search', async () => {
  const user = userEvent.setup(); const onChange = vi.fn(); const onSelect = vi.fn();
  render(<PlaceInput field="start" locale="en" point={undefined} onChange={onChange} onSelect={onSelect} />);
  await user.type(screen.getByRole('combobox', { name: 'Start' }), 'Хайфа');
  expect(onChange).toHaveBeenCalled(); expect(onSelect).not.toHaveBeenCalled();
  await user.keyboard('{ArrowDown}{Enter}');
  expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ latitude: expect.any(Number), longitude: expect.any(Number) }));
  expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
});
it('shows the nearest name for a map point and never substitutes its coordinates', () => {
  const point = { latitude: 32.814107, longitude: 34.995308 }; const onSelect = vi.fn();
  render(<PlaceInput field="destination" locale="ru" point={point} onChange={vi.fn()} onSelect={onSelect} />);
  expect(screen.getByRole('combobox', { name: 'Финиш' })).toHaveValue('Рядом с Haifa');
  expect(onSelect).not.toHaveBeenCalled();
});
it('escape closes suggestions and clearing a selected point invalidates it', async () => {
  const user = userEvent.setup(); const onChange = vi.fn();
  render(<PlaceInput field="start" locale="en" point={{ latitude: 32.8, longitude: 35 }} onChange={onChange} onSelect={vi.fn()} />);
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Haifa' } });
  expect(screen.getByRole('listbox')).toBeInTheDocument();
  await user.keyboard('{Escape}');
  await user.click(screen.getByRole('button', { name: 'Clear start' }));
  expect(onChange).toHaveBeenLastCalledWith();
});
