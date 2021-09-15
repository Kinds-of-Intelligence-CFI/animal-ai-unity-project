using System;
using System.Collections.Generic;
using UnityEngine;

public class SignPosterboard : Prefab
{

    private Material _symbolMat;
    public string selectedSymbolName;

    // Treat the following arrays as if a dictionary := {symbolNames[0]:textures[0], symbolNames[1]:textures[1], ...} so is serializable
    public string[] symbolNames; public Texture[] textures;

    void Awake()
    {
        // attempts to retrieve symbol material (the third of three in current implementation)
        // this needs changing if the implementation of SignPosterboard prefab changes
        _symbolMat = this.gameObject.GetComponent<MeshRenderer>().materials[2];
        if (!_symbolMat.name.Contains("symbol")) { Debug.Log("WARNING: a SignPosterboard may not have found the correct symbol material!!"); }
        // sets texture to show correct chosen symbol according to symbol name provided
        _symbolMat.SetTexture("_BaseMap", getTextureBySymbolName(selectedSymbolName));

    }

    private Texture getTextureBySymbolName(string S) { return textures[Array.IndexOf(symbolNames, S)]; }
}
