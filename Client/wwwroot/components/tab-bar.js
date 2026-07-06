import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/tab-bar.css');

class TabBar extends HTMLElement {
    // observedAttributes is a predefined part of Web Components, forming a pair with attributeChangedCallback to allow listening to these attributes
    static get observedAttributes() {
        return ['active'];
    }

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.render();
        this.shadowRoot.addEventListener('click', (e) => {
            // e.target is the actual element that was clicked, .closest() walks up the DOM looking for the nearest 'data-tab' parent, or null if none found
            const tab = e.target.closest('[data-tab]');
            if (!tab) {
                return;
            }

            this.dispatchEvent(new CustomEvent('tab-change', {
                bubbles: true,
                composed: true,
                detail: {tab: tab.dataset.tab }
            }));
        });
    }

    attributeChangedCallback() {
        this.render();
    }

    render() {
        const activeTab = this.getAttribute('active') || 'oidc';
        const tabs = [
            { id: 'combined', label: 'Login + API' },
            { id: 'oidc', label: 'OIDC Login Flow' },
            { id: 'oauth', label: 'OAuth2.0 API Access' }
        ];

        // Note: :host will access the style of the tab-bar tag itself, this is the proper way to style it from within the shadow root
        this.shadowRoot.innerHTML = `
            <nav>${tabs.map(tab => `
                    <button data-tab="${tab.id}" class="${tab.id === activeTab ? 'active' : ''}">
                        ${tab.label}
                    </button>
                 `).join('')}</nav>
        `;
    }
}

customElements.define('tab-bar', TabBar);