import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/dump-tab.css');

class DumpTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <p class="flow-label">Auth Server - Storage Dump</p>
            <p class="description">
                <strong>Dump all info stored in the Authorization Server</strong> to be viewed here.
                This includes information about users who are currently signed on and what permissions they have been granted,
                as well as all current session information, refresh tokens, consent, etc.
            </p>
            <button class="btn-secondary" id="dump-btn">Dump Auth Info</button>
            <pre id="dump-info"></pre>
        `;

        this.shadowRoot.getElementById('dump-btn')
            .addEventListener('click', this.callDumpEndpoint)
    }

    disconnectedCallback() {
        const dumpBtn = this.shadowRoot.getElementById('dump-btn');
        if (dumpBtn) {
            dumpBtn.removeEventListener('click', this.callDumpEndpoint)
        }
    }

    callDumpEndpoint = async () => {
        const response = await fetch('/bff/dumpeverything');
        const pre = this.shadowRoot.getElementById('dump-info');
        if (!response.ok && pre) {
            pre.textContent = `Error calling the DumpEverything endpoint.  Status: ${response.status}`;
            return;
        }

        const data = await response.json();
        pre.textContent = JSON.stringify(data, null, 2);
    }
}

customElements.define('dump-tab', DumpTab);