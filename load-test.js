import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    load_test: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: 10 },
        { duration: '30s', target: 50 },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500', 'p(99)<1000'],
  },
};

const BASE_URL = 'http://host.docker.internal:8080';

export default function () {
  const payload = JSON.stringify({
    amount: Math.floor(Math.random() * 1000) + 1,
    type: Math.random() > 0.5 ? 'Credit' : 'Debit',
    description: 'Load test entry',
  });

  const params = {
    headers: { 'Content-Type': 'application/json' },
  };

  const res = http.post(`${BASE_URL}/api/entries`, payload, params);

  check(res, {
    'status is 201': (r) => r.status === 201,
    'has entry id': (r) => r.json('id') !== null && r.json('id') !== '',
  });

  sleep(0.1);
}

export function handleSummary(data) {
  return {
    stdout: textSummary(data, { indent: ' ', enableColors: false }),
  };
}
