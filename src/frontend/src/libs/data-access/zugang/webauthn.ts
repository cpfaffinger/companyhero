// WebAuthn im Browser (Zugang 3.1, A-015): die Optionen kommen vom Backend (Fido2NetLib, JSON mit base64url-Feldern), die
// Antwort geht unverändert als WebAuthn-JSON zurück. Bevorzugt die JSON-Hilfsfunktionen der Plattform
// (parseCreationOptionsFromJSON, toJSON); ältere Browser erhalten dieselbe Umwandlung von Hand.

type Json = Record<string, unknown>;

interface CredentialStatic {
  parseCreationOptionsFromJSON?: (json: unknown) => PublicKeyCredentialCreationOptions;
  parseRequestOptionsFromJSON?: (json: unknown) => PublicKeyCredentialRequestOptions;
}

export function webAuthnVerfuegbar(): boolean {
  return typeof PublicKeyCredential !== 'undefined' && typeof navigator.credentials?.create === 'function';
}

export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((value.length + 3) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

export function bytesToBase64Url(buffer: ArrayBuffer | ArrayBufferView): string {
  const bytes = buffer instanceof ArrayBuffer ? new Uint8Array(buffer) : new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let binary = '';
  for (const b of bytes) {
    binary += String.fromCharCode(b);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
}

/** Umwandlung der base64url-Felder in ArrayBuffer für Browser ohne parseCreationOptionsFromJSON. */
export function creationOptionsFromJson(json: Json): PublicKeyCredentialCreationOptions {
  const user = json['user'] as Json;
  return {
    rp: json['rp'] as PublicKeyCredentialRpEntity,
    user: { ...(user as unknown as PublicKeyCredentialUserEntity), id: base64UrlToBytes(user['id'] as string) },
    challenge: base64UrlToBytes(json['challenge'] as string),
    pubKeyCredParams: json['pubKeyCredParams'] as PublicKeyCredentialParameters[],
    timeout: json['timeout'] as number | undefined,
    attestation: json['attestation'] as AttestationConveyancePreference | undefined,
    authenticatorSelection: json['authenticatorSelection'] as AuthenticatorSelectionCriteria | undefined,
    excludeCredentials: descriptors(json['excludeCredentials']),
  };
}

export function requestOptionsFromJson(json: Json): PublicKeyCredentialRequestOptions {
  return {
    challenge: base64UrlToBytes(json['challenge'] as string),
    timeout: json['timeout'] as number | undefined,
    rpId: json['rpId'] as string | undefined,
    userVerification: json['userVerification'] as UserVerificationRequirement | undefined,
    allowCredentials: descriptors(json['allowCredentials']),
  };
}

function descriptors(value: unknown): PublicKeyCredentialDescriptor[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }
  return value.map((d: Json) => ({ type: 'public-key', id: base64UrlToBytes(d['id'] as string), transports: d['transports'] as AuthenticatorTransport[] | undefined }));
}

/** Antwort im WebAuthn-JSON-Format, wie es das Backend (Fido2NetLib) erwartet. */
export function credentialToJson(credential: PublicKeyCredential): Json {
  const withJson = credential as PublicKeyCredential & { toJSON?: () => unknown };
  if (typeof withJson.toJSON === 'function') {
    return withJson.toJSON() as unknown as Json;
  }
  const response = credential.response as AuthenticatorAttestationResponse & AuthenticatorAssertionResponse;
  const body: Json = { clientDataJSON: bytesToBase64Url(response.clientDataJSON) };
  if ('attestationObject' in response && response.attestationObject) {
    body['attestationObject'] = bytesToBase64Url(response.attestationObject);
    body['transports'] = typeof response.getTransports === 'function' ? response.getTransports() : [];
  } else {
    body['authenticatorData'] = bytesToBase64Url(response.authenticatorData);
    body['signature'] = bytesToBase64Url(response.signature);
    body['userHandle'] = response.userHandle ? bytesToBase64Url(response.userHandle) : null;
  }
  return {
    id: credential.id,
    rawId: bytesToBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? null,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: body,
  };
}

/** Registrierung: Optionen des Backends → Authenticator → JSON-Antwort für das Backend. */
export async function passkeyErstellen(options: unknown): Promise<Json> {
  const statics = PublicKeyCredential as unknown as CredentialStatic;
  const publicKey = statics.parseCreationOptionsFromJSON ? statics.parseCreationOptionsFromJSON(options) : creationOptionsFromJson(options as Json);
  const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null;
  if (!credential) {
    throw new Error('passkey_abgebrochen');
  }
  return credentialToJson(credential);
}

/** Anmeldung: Optionen des Backends → Authenticator → JSON-Antwort für das Backend. */
export async function passkeyVerwenden(options: unknown): Promise<Json> {
  const statics = PublicKeyCredential as unknown as CredentialStatic;
  const publicKey = statics.parseRequestOptionsFromJSON ? statics.parseRequestOptionsFromJSON(options) : requestOptionsFromJson(options as Json);
  const credential = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential | null;
  if (!credential) {
    throw new Error('passkey_abgebrochen');
  }
  return credentialToJson(credential);
}
