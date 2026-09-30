export function trackName(route: { name?: string | null }, index: number): string {
  const name = route.name;
  return typeof name === 'string' && name.length <= 120 && /^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$/.test(name)
    ? name : `cycling-route-${index + 1}`;
}
