// The api=caller element exposes a button that allows a user to attempt to call a protected API
class ApiCaller extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open'});
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                button {
                    font-family: 'Inter', sans-serif;
                    font-size: 13px;
                    font-wright: 500;
                    padding: 9px 18px;
                    border-radius: 6px;
                    background: transparent;
                    color: #C0C0D8;
                    border: 1px solid #2A2D3A;
                    cursor: pointer;
                    transition: all 0.15s;
                    margin-bottom: 16px;
                }
                button:hover {
                    border-color: #7C6AFE;
                    color: #E0E0F0;
                }
                pre {
                    background: #1A1D27;
                    border: 1px solid #2A2D3A;
                    border-radius: 6px;
                    padding: 16px;
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 12px;
                    color: #C0C0D8;
                    line-height: 1.6;
                    overflow-x: auto;
                    min-height: 48px;
                    white-space: pre-wrap;
                }
                pre:empty::before {
                    content: 'Response will appear here';
                    color: #4A4D5E;
                }
            </style>
            <button id="call-btn">Call Protected API</button>
            <pre id="api-call-result"></pre>
        `;
        this.shadowRoot.getElementById('call-btn')
            .addEventListener('click', () => this.callApi());
    }

    async callApi() {
        const response = await fetch('/bff/protected');
        if (!response.ok) {
            this.shadowRoot.getElementById('api-call-result').textContent = `Error calling protected API: Status was: ${response.status}`;
        }

        const data = await response.json();
        this.shadowRoot.getElementById('api-call-result').textContent = JSON.stringify(data, null, 2);
    }
}

customElements.define('api-caller', ApiCaller);