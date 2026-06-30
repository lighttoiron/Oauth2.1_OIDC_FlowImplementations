// The api=caller element exposes a button that allows a user to attempt to call a protected API
class ApiCaller extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open'});
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
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