export function randomUrlSafe(_byteLength = 32): string {
  return 'verifier';
}

export async function challengeS256(_verifier: string): Promise<string> {
  return 'challenge';
}
