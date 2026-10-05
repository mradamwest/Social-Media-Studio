"""Exercise broker session guards locally, without contacting any social network."""
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None

class ConnectionChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with socket.socket() as s:
            s.bind(('127.0.0.1', 0))
            port = s.getsockname()[1]
        cls.base = f'http://127.0.0.1:{port}'
        env = dict(os.environ, ASPNETCORE_URLS=cls.base,
                   CONNECTION_SERVICE_PUBLIC_URL='https://connections.example',
                   SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_ID='test-client',
                   SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_SECRET='test-secret')
        root = Path(__file__).resolve().parents[1]
        dll = root / 'src/SocialMediaStudio.ConnectionService/bin/Release/net8.0/SocialMediaStudio.ConnectionService.dll'
        cls.process = subprocess.Popen(['dotnet', str(dll)], env=env,
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        cls.opener = urllib.request.build_opener(NoRedirect())
        for _ in range(100):
            if cls.process.poll() is not None:
                raise RuntimeError('Connection service exited during startup')
            try:
                cls.call('/sessions/nonexistent')
                break
            except OSError:
                time.sleep(.1)
        else:
            cls.process.terminate()
            raise RuntimeError('Connection service did not start')

    @classmethod
    def tearDownClass(cls):
        cls.process.terminate()
        cls.process.wait(timeout=10)

    @classmethod
    def call(cls, path, payload=None, proof=None):
        headers = {}
        if proof is not None:
            headers['X-Connection-Proof'] = proof
        data = None
        if payload is not None:
            data = json.dumps(payload).encode()
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(cls.base + path, data=data, headers=headers)
        try:
            response = cls.opener.open(request, timeout=5)
        except urllib.error.HTTPError as ex:
            response = ex
        with response:
            return response.code, response.read(), response.headers

    def start(self):
        proof = 'A' * 64
        status, body, headers = self.call('/sessions', {'provider': 'YouTube', 'proofHash': hashlib.sha256(proof.encode()).hexdigest()})
        self.assertEqual(status, 200)
        self.assertEqual(headers['Cache-Control'], 'no-store')
        session = json.loads(body)
        self.assertTrue(session['authorizationUrl'].startswith('https://connections.example/authorize/'))
        self.assertNotIn(proof, session['authorizationUrl'])
        return session['sessionId'], proof

    def test_reject_invalid_provider(self):
        self.assertEqual(self.call('/sessions', {'provider': 'Unknown', 'proofHash': 'A' * 64})[0], 400)

    def test_reject_invalid_hash(self):
        self.assertEqual(self.call('/sessions', {'provider': 'YouTube', 'proofHash': 'invalid'})[0], 400)

    def test_require_session_proof(self):
        session, proof = self.start()
        self.assertEqual(self.call('/sessions/' + session)[0], 401)
        self.assertEqual(self.call('/sessions/' + session, proof='B' * 64)[0], 401)
        self.assertEqual(self.call('/sessions/' + session, proof=proof)[0], 202)

    def test_registered_callback_and_pkce(self):
        session, _ = self.start()
        status, _, headers = self.call('/authorize/' + session)
        self.assertEqual(status, 302)
        target = urllib.parse.urlparse(headers['Location'])
        self.assertEqual(target.hostname, 'accounts.google.com')
        query = urllib.parse.parse_qs(target.query)
        self.assertEqual(query['redirect_uri'], ['https://connections.example/oauth/callback/YouTube'])
        self.assertEqual(query['code_challenge_method'], ['S256'])
        self.assertTrue(query['state'][0])
        self.assertNotIn('client_secret', query)

    def test_reject_unbound_callback(self):
        self.assertEqual(self.call('/oauth/callback/YouTube?state=incorrect&code=test')[0], 400)

    def test_declined_session(self):
        session, proof = self.start()
        _, _, headers = self.call('/authorize/' + session)
        state = urllib.parse.parse_qs(urllib.parse.urlparse(headers['Location']).query)['state'][0]
        self.assertEqual(self.call('/oauth/callback/Facebook?state=' + state + '&error=access_denied')[0], 400)
        self.assertEqual(self.call('/oauth/callback/YouTube?state=' + state + '&error=access_denied')[0], 200)
        self.assertEqual(self.call('/sessions/' + session, proof=proof)[0], 403)
        self.assertEqual(self.call('/sessions/' + session, proof=proof)[0], 404)

if __name__ == '__main__':
    unittest.main()
