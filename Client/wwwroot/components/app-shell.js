import "./tab-bar.js";
import "./oidc-flow-tab.js";
import "./oauth-api-tab.js";
import { loadBaseSheets } from './styles/loader.js';

const baseSheets = await loadBaseSheets();

class AppShell extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets];
        this._activeTab = 'oidc';
    }

    connectedCallback() {
        this.render();
        this.addEventListener('tab-change', (e) => {
            this._activeTab = e.detail.tab;
            this.render();
        })
    }

    render() {
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                tab-bar { display: block; }
                .panel { display: none; }
                .panel.active {display: block; }
            </style>
            <tab-bar active="${this._activeTab}"></tab-bar>
            <div class="panel ${this._activeTab === 'oidc' ? 'active' : ''}">
                <oidc-flow-tab></oidc-flow-tab>
            </div>
            <div class="panel ${this._activeTab === "oauth" ? 'active' : ''}">
                <oauth-api-tab></oauth-api-tab>
            </div>
        `;
    }
}

customElements.define('app-shell', AppShell);