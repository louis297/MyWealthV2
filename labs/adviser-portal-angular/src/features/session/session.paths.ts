export function isInAppPath(path: string | null | undefined): path is string {
  if (!path || !path.startsWith('/') || path.startsWith('//')) {
    return false;
  }

  const pathname = path.split('?')[0] ?? '';
  return (
    pathname === '/' ||
    pathname === '/profile' ||
    pathname === '/session' ||
    pathname === '/login' ||
    pathname === '/callback' ||
    pathname === '/forbidden' ||
    pathname === '/customers' ||
    pathname.startsWith('/customers/') ||
    pathname === '/advisers' ||
    pathname.startsWith('/advisers/')
  );
}

export function isReturnPath(path: string | null | undefined): path is string {
  if (!isInAppPath(path)) {
    return false;
  }

  const pathname = path.split('?')[0] ?? '';
  return pathname !== '/session' && pathname !== '/login' && pathname !== '/callback';
}
