export function isInAppPath(path: string | null | undefined): path is string {
  if (!path || !path.startsWith('/') || path.startsWith('//')) {
    return false;
  }

  const pathname = path.split('?')[0] ?? '';
  return (
    pathname === '/' ||
    pathname === '/profile' ||
    pathname === '/session' ||
    pathname === '/forbidden' ||
    pathname === '/customers' ||
    pathname.startsWith('/customers/') ||
    pathname === '/advisers' ||
    pathname.startsWith('/advisers/')
  );
}
