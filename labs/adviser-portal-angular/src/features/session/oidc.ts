import { RETURN_TO_KEY } from './session.storage';

export async function startAuthorize(_authority: string): Promise<void> {
  sessionStorage.setItem(RETURN_TO_KEY, window.location.pathname);
}
