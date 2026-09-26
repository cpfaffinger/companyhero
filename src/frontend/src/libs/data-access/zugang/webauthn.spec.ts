import { base64UrlToBytes, bytesToBase64Url, creationOptionsFromJson, credentialToJson, requestOptionsFromJson } from './webauthn';

describe('WebAuthn-Hülle (Zugang 3.1, A-015)', () => {
  it('base64url ohne Auffüllung ist verlustfrei in beide Richtungen', () => {
    const bytes = new Uint8Array([0, 1, 2, 250, 251, 252, 253, 254, 255]);
    const encoded = bytesToBase64Url(bytes);
    expect(encoded).not.toMatch(/[+/=]/);
    expect(Array.from(base64UrlToBytes(encoded))).toEqual(Array.from(bytes));
  });

  it('wandelt die Optionen des Backends (Fido2NetLib-JSON) in WebAuthn-Optionen mit Byte-Feldern um', () => {
    const creation = creationOptionsFromJson({
      rp: { id: 'localhost', name: 'CompanyHero' },
      user: { id: bytesToBase64Url(new Uint8Array([1, 2, 3])), name: 'Anna', displayName: 'Anna' },
      challenge: bytesToBase64Url(new Uint8Array([9, 8, 7])),
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      timeout: 60000,
      attestation: 'none',
      authenticatorSelection: { residentKey: 'required', userVerification: 'preferred' },
      excludeCredentials: [{ type: 'public-key', id: bytesToBase64Url(new Uint8Array([4, 4])) }],
      status: 'ok',
      errorMessage: '',
    });
    expect(Array.from(creation.user.id as Uint8Array)).toEqual([1, 2, 3]);
    expect(Array.from(creation.challenge as Uint8Array)).toEqual([9, 8, 7]);
    expect(Array.from(creation.excludeCredentials![0].id as Uint8Array)).toEqual([4, 4]);
    expect(creation.pubKeyCredParams[0].alg).toBe(-7);

    const request = requestOptionsFromJson({ challenge: bytesToBase64Url(new Uint8Array([5])), rpId: 'localhost', userVerification: 'preferred', allowCredentials: [] });
    expect(Array.from(request.challenge as Uint8Array)).toEqual([5]);
    expect(request.allowCredentials).toEqual([]);
  });

  it('gibt die Antwort im WebAuthn-JSON-Format zurück, das das Backend erwartet (id, rawId, type, response.*)', () => {
    const rawId = new Uint8Array([10, 11]).buffer;
    const credential = {
      id: bytesToBase64Url(rawId),
      rawId,
      type: 'public-key',
      authenticatorAttachment: 'platform',
      getClientExtensionResults: () => ({}),
      response: {
        clientDataJSON: new Uint8Array([1]).buffer,
        authenticatorData: new Uint8Array([2]).buffer,
        signature: new Uint8Array([3]).buffer,
        userHandle: new Uint8Array([4]).buffer,
      },
    } as unknown as PublicKeyCredential;
    const json = credentialToJson(credential);
    expect(json['id']).toBe('Cgs');
    expect(json['rawId']).toBe('Cgs');
    expect(json['type']).toBe('public-key');
    expect(json['response']).toEqual({ clientDataJSON: 'AQ', authenticatorData: 'Ag', signature: 'Aw', userHandle: 'BA' });
  });
});
