/*
* Copyright (c) 2012-2020 AssimpNet - Nicholas Woodfield
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
* THE SOFTWARE.
*/

using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Assimp.Unmanaged
{
    /// <summary>
    /// Singleton that governs access to the unmanaged Assimp library functions.
    /// </summary>
    public static partial class AssimpLibrary
    {
        const string DLL_NAME = "assimp";

        private static bool m_enableVerboseLogging = false;

        /// <summary>
        /// Gets if the Assimp unmanaged library supports multithreading. If it was compiled for single threading only,
        /// then it will not utilize multiple threads during import.
        /// </summary>
        public static bool IsMultithreadingSupported => !((GetCompileFlags() & CompileFlags.SingleThreaded) == CompileFlags.SingleThreaded);

        #region Import Methods

        /// <summary>
        /// Imports a file.
        /// </summary>
        /// <param name="file">Valid filename</param>
        /// <param name="flags">Post process flags specifying what steps are to be run after the import.</param>
        /// <param name="propStore">Property store containing config name-values, may be null.</param>
        /// <returns>Pointer to the unmanaged data structure.</returns>
        public static IntPtr ImportFile(string file, PostProcessSteps flags, IntPtr propStore)
        {
            return ImportFile(file, flags, IntPtr.Zero, propStore);
        }

        /// <summary>
        /// Imports a file.
        /// </summary>
        /// <param name="file">Valid filename</param>
        /// <param name="flags">Post process flags specifying what steps are to be run after the import.</param>
        /// <param name="fileIO">Pointer to an instance of AiFileIO, a custom file IO system used to open the model and
        /// any associated file the loader needs to open, passing NULL uses the default implementation.</param>
        /// <param name="propStore">Property store containing config name-values, may be null.</param>
        /// <returns>Pointer to the unmanaged data structure.</returns>
        public static IntPtr ImportFile(string file, PostProcessSteps flags, IntPtr fileIO, IntPtr propStore)
        {
            var scenePtr = aiImportFileExWithProperties(file, (uint)flags, fileIO, propStore);
            FixQuaternionsInSceneFromAssimp(scenePtr);
            return scenePtr;
        }


        /// <summary>
        /// Imports a scene from a stream. This uses the "aiImportFileFromMemory" function. The stream can be from anyplace,
        /// not just a memory stream. It is up to the caller to dispose of the stream.
        /// </summary>
        /// <param name="stream">Stream containing the scene data</param>
        /// <param name="flags">Post processing flags</param>
        /// <param name="formatHint">A hint to Assimp to decide which importer to use to process the data</param>
        /// <param name="propStore">Property store containing the config name-values, may be null.</param>
        /// <returns>Pointer to the unmanaged data structure.</returns>
        public static IntPtr ImportFileFromStream(Stream stream, PostProcessSteps flags, string formatHint, IntPtr propStore)
        {
            var buffer = MemoryHelper.ReadStreamFully(stream, 0);

            var scenePtr = aiImportFileFromMemoryWithProperties(buffer, (uint)buffer.Length, (uint)flags, formatHint, propStore);
            FixQuaternionsInSceneFromAssimp(scenePtr);
            return scenePtr;
        }

        /// <summary>
        /// Releases the unmanaged scene data structure. This should NOT be used for unmanaged scenes that were marshaled
        /// from the managed scene structure - only for scenes whose memory was allocated by the native library!
        /// </summary>
        /// <param name="scene">Pointer to the unmanaged scene data structure.</param>
        public static void ReleaseImport(IntPtr scene)
        {
            if (scene == IntPtr.Zero)
                return;

            aiReleaseImport(scene);
        }

        /// <summary>
        /// Applies a post-processing step on an already imported scene.
        /// </summary>
        /// <param name="scene">Pointer to the unmanaged scene data structure.</param>
        /// <param name="flags">Post processing steps to run.</param>
        /// <returns>Pointer to the unmanaged scene data structure.</returns>
        public static IntPtr ApplyPostProcessing(IntPtr scene, PostProcessSteps flags)
        {
            if (scene == IntPtr.Zero)
                return IntPtr.Zero;

            FixQuaternionsInSceneToAssimp(scene);
            var scenePtr = aiApplyPostProcessing(scene, (uint)flags);
            FixQuaternionsInSceneFromAssimp(scenePtr);
            return scenePtr;
        }
        #endregion

        #region Export Methods

        /// <summary>
        /// Gets all supported export formats.
        /// </summary>
        /// <returns>Array of supported export formats.</returns>
        public static ExportFormatDescription[] GetExportFormatDescriptions()
        {
            var count = (int)aiGetExportFormatCount().ToUInt32();

            if (count == 0)
                return Array.Empty<ExportFormatDescription>();

            ExportFormatDescription[] descriptions = new ExportFormatDescription[count];

            for (int i = 0; i < count; i++)
            {
                IntPtr formatDescPtr = aiGetExportFormatDescription(new UIntPtr((uint)i));
                if (formatDescPtr != IntPtr.Zero)
                {
                    AiExportFormatDesc desc = MemoryHelper.Read<AiExportFormatDesc>(formatDescPtr);
                    descriptions[i] = new ExportFormatDescription(desc);

                    aiReleaseExportFormatDescription(formatDescPtr);
                }
            }

            return descriptions;
        }

        /// <summary>
        /// Exports the given scene to a chosen file format. Returns the exported data as a binary blob which you can embed into another data structure or file.
        /// </summary>
        /// <param name="scene">Scene to export, it is the responsibility of the caller to free this when finished.</param>
        /// <param name="formatId">Format id describing which format to export to.</param>
        /// <param name="preProcessing">Pre processing flags to operate on the scene during the export.</param>
        /// <returns>Exported binary blob, or null if there was an error.</returns>
        public static ExportDataBlob ExportSceneToBlob(IntPtr scene, string formatId, PostProcessSteps preProcessing)
        {
            if (string.IsNullOrEmpty(formatId) || scene == IntPtr.Zero)
                return null;

            FixQuaternionsInSceneToAssimp(scene);
            IntPtr blobPtr = aiExportSceneToBlob(scene, formatId, (uint)preProcessing);
            FixQuaternionsInSceneFromAssimp(scene);

            if (blobPtr == IntPtr.Zero)
                return null;

            AiExportDataBlob blob = MemoryHelper.Read<AiExportDataBlob>(blobPtr);
            ExportDataBlob dataBlob = new ExportDataBlob(ref blob);
            aiReleaseExportBlob(blobPtr);

            return dataBlob;
        }

        /// <summary>
        /// Exports the given scene to a chosen file format and writes the result file(s) to disk.
        /// </summary>
        /// <param name="scene">The scene to export, which needs to be freed by the caller. The scene is expected to conform to Assimp's Importer output format. In short,
        /// this means the model data should use a right handed coordinate system, face winding should be counter clockwise, and the UV coordinate origin assumed to be upper left. If the input is different, specify the pre processing flags appropiately.</param>
        /// <param name="formatId">Format id describing which format to export to.</param>
        /// <param name="fileName">Output filename to write to</param>
        /// <param name="preProcessing">Pre processing flags - accepts any post processing step flag. In reality only a small subset are actually supported, e.g. to ensure the input
        /// conforms to the standard Assimp output format. Some may be redundant, such as triangulation, which some exporters may have to enforce due to the export format.</param>
        /// <returns>Return code specifying if the operation was a success.</returns>
        public static ReturnCode ExportScene(IntPtr scene, string formatId, string fileName, PostProcessSteps preProcessing)
        {
            return ExportScene(scene, formatId, fileName, IntPtr.Zero, preProcessing);
        }

        /// <summary>
        /// Exports the given scene to a chosen file format and writes the result file(s) to disk.
        /// </summary>
        /// <param name="scene">The scene to export, which needs to be freed by the caller. The scene is expected to conform to Assimp's Importer output format. In short,
        /// this means the model data should use a right handed coordinate system, face winding should be counter clockwise, and the UV coordinate origin assumed to be upper left. If the input is different, specify the pre processing flags appropiately.</param>
        /// <param name="formatId">Format id describing which format to export to.</param>
        /// <param name="fileName">Output filename to write to</param>
        /// <param name="fileIO">Pointer to an instance of AiFileIO, a custom file IO system used to open the model and
        /// any associated file the loader needs to open, passing NULL uses the default implementation.</param>
        /// <param name="preProcessing">Pre processing flags - accepts any post processing step flag. In reality only a small subset are actually supported, e.g. to ensure the input
        /// conforms to the standard Assimp output format. Some may be redundant, such as triangulation, which some exporters may have to enforce due to the export format.</param>
        /// <returns>Return code specifying if the operation was a success.</returns>
        public static ReturnCode ExportScene(IntPtr scene, string formatId, string fileName, IntPtr fileIO, PostProcessSteps preProcessing)
        {
            if (string.IsNullOrEmpty(formatId) || scene == IntPtr.Zero)
                return ReturnCode.Failure;

            FixQuaternionsInSceneToAssimp(scene);
            var ret = aiExportSceneEx(scene, formatId, fileName, fileIO, (uint)preProcessing);
            FixQuaternionsInSceneFromAssimp(scene);
            return ret;
        }

        /// <summary>
        /// Creates a modifyable copy of a scene, useful for copying the scene that was imported so its topology can be modified
        /// and the scene be exported.
        /// </summary>
        /// <param name="sceneToCopy">Valid scene to be copied</param>
        /// <returns>Modifyable copy of the scene</returns>
        public static IntPtr CopyScene(IntPtr sceneToCopy)
        {
            if (sceneToCopy == IntPtr.Zero)
                return IntPtr.Zero;

            aiCopyScene(sceneToCopy, out var copiedScene);
            return copiedScene;
        }

        #endregion

        #region Logging Methods

        /// <summary>
        /// Attaches a log stream callback to catch Assimp messages.
        /// </summary>
        /// <param name="logStreamPtr">Pointer to an instance of AiLogStream.</param>
        public static void AttachLogStream(IntPtr logStreamPtr)
        {
            aiAttachLogStream(logStreamPtr);
        }

        /// <summary>
        /// Enables verbose logging.
        /// </summary>
        /// <param name="enable">True if verbose logging is to be enabled or not.</param>
        public static void EnableVerboseLogging(bool enable)
        {
            aiEnableVerboseLogging(enable);

            m_enableVerboseLogging = enable;
        }


        /// <summary>
        /// Gets if verbose logging is enabled.
        /// </summary>
        /// <returns>True if verbose logging is enabled, false otherwise.</returns>
        public static bool GetVerboseLoggingEnabled()
        {
            return m_enableVerboseLogging;
        }

        /// <summary>
        /// Detaches a logstream callback.
        /// </summary>
        /// <param name="logStreamPtr">Pointer to an instance of AiLogStream.</param>
        /// <returns>A return code signifying if the function was successful or not.</returns>
        public static ReturnCode DetachLogStream(IntPtr logStreamPtr)
        {
            return aiDetachLogStream(logStreamPtr);
        }

        /// <summary>
        /// Detaches all logstream callbacks currently attached to Assimp.
        /// </summary>
        public static void DetachAllLogStreams()
        {
            aiDetachAllLogStreams();
        }

        #endregion

        #region Import Properties Setters

        /// <summary>
        /// Create an empty property store. Property stores are used to collect import settings.
        /// </summary>
        /// <returns>Pointer to property store</returns>
        public static IntPtr CreatePropertyStore()
        {
            return aiCreatePropertyStore();
        }

        /// <summary>
        /// Deletes a property store.
        /// </summary>
        /// <param name="propertyStore">Pointer to property store</param>
        public static void ReleasePropertyStore(IntPtr propertyStore)
        {
            if (propertyStore == IntPtr.Zero)
                return;

            aiReleasePropertyStore(propertyStore);
        }

        /// <summary>
        /// Sets an integer property value.
        /// </summary>
        /// <param name="propertyStore">Pointer to property store</param>
        /// <param name="name">Property name</param>
        /// <param name="value">Property value</param>
        public static void SetImportPropertyInteger(IntPtr propertyStore, string name, int value)
        {
            if (propertyStore == IntPtr.Zero || string.IsNullOrEmpty(name))
                return;

            aiSetImportPropertyInteger(propertyStore, name, value);
        }

        /// <summary>
        /// Sets a float property value.
        /// </summary>
        /// <param name="propertyStore">Pointer to property store</param>
        /// <param name="name">Property name</param>
        /// <param name="value">Property value</param>
        public static void SetImportPropertyFloat(IntPtr propertyStore, string name, float value)
        {
            if (propertyStore == IntPtr.Zero || string.IsNullOrEmpty(name))
                return;

            aiSetImportPropertyFloat(propertyStore, name, value);
        }

        /// <summary>
        /// Sets a string property value.
        /// </summary>
        /// <param name="propertyStore">Pointer to property store</param>
        /// <param name="name">Property name</param>
        /// <param name="value">Property value</param>
        public static void SetImportPropertyString(IntPtr propertyStore, string name, string value)
        {
            if (propertyStore == IntPtr.Zero || string.IsNullOrEmpty(name))
                return;

            var str = new AiString();
            if (str.SetString(value))
                aiSetImportPropertyString(propertyStore, name, ref str);
        }

        /// <summary>
        /// Sets a matrix property value.
        /// </summary>
        /// <param name="propertyStore">Pointer to property store</param>
        /// <param name="name">Property name</param>
        /// <param name="value">Property value</param>
        public static void SetImportPropertyMatrix(IntPtr propertyStore, string name, Matrix4x4 value)
        {
            if (propertyStore == IntPtr.Zero || string.IsNullOrEmpty(name))
                return;

            aiSetImportPropertyMatrix(propertyStore, name, ref value);
        }

        #endregion

        #region Material Getters

        /// <summary>
        /// Retrieves a color value from the material property table.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="key">Ai mat key (base) name to search for</param>
        /// <param name="texType">Texture Type semantic, always zero for non-texture properties</param>
        /// <param name="texIndex">Texture index, always zero for non-texture properties</param>
        /// <returns>The color if it exists. If not, the default Vector4 value is returned.</returns>
        public static Vector4 GetMaterialColor(ref AiMaterial mat, string key, TextureType texType, uint texIndex)
        {
            var ptr = IntPtr.Zero;
            try
            {
                ptr = MemoryHelper.AllocateMemory(MemoryHelper.SizeOf<Vector4>());
                var code = aiGetMaterialColor(ref mat, key, (uint)texType, texIndex, ptr);
                var color = new Vector4();
                if (code == ReturnCode.Success && ptr != IntPtr.Zero)
                    color = MemoryHelper.Read<Vector4>(ptr);

                return color;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    MemoryHelper.FreeMemory(ptr);
            }
        }

        /// <summary>
        /// Retrieves an array of float values with the specific key from the material.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="key">Ai mat key (base) name to search for</param>
        /// <param name="texType">Texture Type semantic, always zero for non-texture properties</param>
        /// <param name="texIndex">Texture index, always zero for non-texture properties</param>
        /// <param name="floatCount">The maximum number of floats to read. This may not accurately describe the data returned, as it may not exist or be smaller. If this value is less than
        /// the available floats, then only the requested number is returned (e.g. 1 or 2 out of a 4 float array).</param>
        /// <returns>The float array, if it exists</returns>
        public static float[] GetMaterialFloatArray(ref AiMaterial mat, string key, TextureType texType, uint texIndex, uint floatCount)
        {
            var ptr = IntPtr.Zero;
            try
            {
                ptr = MemoryHelper.AllocateMemory(IntPtr.Size);
                var code = aiGetMaterialFloatArray(ref mat, key, (uint)texType, texIndex, ptr, ref floatCount);
                float[] array = null;
                if (code == ReturnCode.Success && floatCount > 0)
                {
                    array = new float[floatCount];
                    MemoryHelper.Read(ptr, array, 0, (int)floatCount);
                }
                return array;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    MemoryHelper.FreeMemory(ptr);
                }
            }
        }

        /// <summary>
        /// Retrieves an array of integer values with the specific key from the material.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="key">Ai mat key (base) name to search for</param>
        /// <param name="texType">Texture Type semantic, always zero for non-texture properties</param>
        /// <param name="texIndex">Texture index, always zero for non-texture properties</param>
        /// <param name="intCount">The maximum number of integers to read. This may not accurately describe the data returned, as it may not exist or be smaller. If this value is less than
        /// the available integers, then only the requested number is returned (e.g. 1 or 2 out of a 4 float array).</param>
        /// <returns>The integer array, if it exists</returns>
        public static int[] GetMaterialIntegerArray(ref AiMaterial mat, string key, TextureType texType, uint texIndex, uint intCount)
        {
            var ptr = IntPtr.Zero;
            try
            {
                ptr = MemoryHelper.AllocateMemory(IntPtr.Size);
                var code = aiGetMaterialIntegerArray(ref mat, key, (uint)texType, texIndex, ptr, ref intCount);
                int[] array = null;
                if (code == ReturnCode.Success && intCount > 0)
                {
                    array = new int[intCount];
                    MemoryHelper.Read(ptr, array, 0, (int)intCount);
                }
                return array;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    MemoryHelper.FreeMemory(ptr);
                }
            }
        }

        /// <summary>
        /// Retrieves a material property with the specific key from the material.
        /// </summary>
        /// <param name="mat">Material to retrieve the property from</param>
        /// <param name="key">Ai mat key (base) name to search for</param>
        /// <param name="texType">Texture Type semantic, always zero for non-texture properties</param>
        /// <param name="texIndex">Texture index, always zero for non-texture properties</param>
        /// <returns>The material property, if found.</returns>
        public static AiMaterialProperty GetMaterialProperty(ref AiMaterial mat, string key, TextureType texType, uint texIndex)
        {
            var code = aiGetMaterialProperty(ref mat, key, (uint)texType, texIndex, out var ptr);
            var prop = new AiMaterialProperty();
            if (code == ReturnCode.Success && ptr != IntPtr.Zero)
                prop = MemoryHelper.Read<AiMaterialProperty>(ptr);

            return prop;
        }

        /// <summary>
        /// Retrieves a string from the material property table.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="key">Ai mat key (base) name to search for</param>
        /// <param name="texType">Texture Type semantic, always zero for non-texture properties</param>
        /// <param name="texIndex">Texture index, always zero for non-texture properties</param>
        /// <returns>The string, if it exists. If not, an empty string is returned.</returns>
        public static string GetMaterialString(ref AiMaterial mat, string key, TextureType texType, uint texIndex)
        {
            var code = aiGetMaterialString(ref mat, key, (uint)texType, texIndex, out var str);
            if (code == ReturnCode.Success)
                return str.GetString();

            return string.Empty;
        }

        /// <summary>
        /// Gets the number of textures contained in the material for a particular texture type.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="type">Texture Type semantic</param>
        /// <returns>The number of textures for the type.</returns>
        public static uint GetMaterialTextureCount(ref AiMaterial mat, TextureType type)
        {
            return aiGetMaterialTextureCount(ref mat, type);
        }

        /// <summary>
        /// Gets the texture filepath contained in the material.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="type">Texture type semantic</param>
        /// <param name="index">Texture index</param>
        /// <returns>The texture filepath, if it exists. If not an empty string is returned.</returns>
        public static string GetMaterialTextureFilePath(ref AiMaterial mat, TextureType type, uint index)
        {
            AiString str;
            TextureMapping mapping;
            uint uvIndex;
            float blendFactor;
            TextureOperation texOp;
            TextureWrapMode[] wrapModes = new TextureWrapMode[2];
            uint flags;

            var code = aiGetMaterialTexture(ref mat, type, index, out str, out mapping, out uvIndex, out blendFactor, out texOp, wrapModes, out flags);
            if (code is not ReturnCode.Success) { return string.Empty; }

            return str.GetString();
        }

        /// <summary>
        /// Gets all values pertaining to a particular texture from a material.
        /// </summary>
        /// <param name="mat">Material to retrieve the data from</param>
        /// <param name="type">Texture type semantic</param>
        /// <param name="index">Texture index</param>
        /// <returns>Returns the texture slot struct containing all the information.</returns>
        public static TextureSlot GetMaterialTexture(ref AiMaterial mat, TextureType type, uint index)
        {
            AiString str;
            TextureMapping mapping;
            uint uvIndex;
            float blendFactor;
            TextureOperation texOp;
            TextureWrapMode[] wrapModes = new TextureWrapMode[2];
            uint flags;

            var code = aiGetMaterialTexture(ref mat, type, index, out str, out mapping, out uvIndex, out blendFactor, out texOp, wrapModes, out flags);

            return new TextureSlot(str.GetString(), type, (int)index, mapping, (int)uvIndex, blendFactor, texOp, wrapModes[0], wrapModes[1], (int)flags);
        }

        #endregion

        #region Error and Info Methods

        /// <summary>
        /// Gets the last error logged in Assimp.
        /// </summary>
        /// <returns>The last error message logged.</returns>
        public static string GetErrorString()
        {
            var ptr = aiGetErrorString();

            if (ptr == IntPtr.Zero)
                return string.Empty;

            return Marshal.PtrToStringAnsi(ptr);
        }

        /// <summary>
        /// Checks whether the model format extension is supported by Assimp.
        /// </summary>
        /// <param name="extension">Model format extension, e.g. ".3ds"</param>
        /// <returns>True if the format is supported, false otherwise.</returns>
        public static bool IsExtensionSupported(string extension)
        {
            return aiIsExtensionSupported(extension);
        }

        /// <summary>
        /// Gets all the model format extensions that are currently supported by Assimp.
        /// </summary>
        /// <returns>Array of supported format extensions</returns>
        public static string[] GetExtensionList()
        {
            var aiString = new AiString();
            aiGetExtensionList(ref aiString);
            return aiString.GetString().Split(["*", ";*"], StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Gets a collection of importer descriptions that detail metadata and feature support for each importer.
        /// </summary>
        /// <returns>Collection of importer descriptions</returns>
        public static ImporterDescription[] GetImporterDescriptions()
        {
            var count = (int)aiGetImportFormatCount().ToUInt32();
            var descrs = new ImporterDescription[count];

            for (var i = 0; i < count; i++)
            {
                var descrPtr = aiGetImportFormatDescription(new UIntPtr((uint)i));
                if (descrPtr != IntPtr.Zero)
                {
                    ref var descr = ref MemoryHelper.AsRef<AiImporterDesc>(descrPtr);
                    descrs[i] = new(descr);
                }
            }

            return descrs;
        }

        /// <summary>
        /// Gets the memory requirements of the scene.
        /// </summary>
        /// <param name="scene">Pointer to the unmanaged scene data structure.</param>
        /// <returns>The memory information about the scene.</returns>
        public static AiMemoryInfo GetMemoryRequirements(IntPtr scene)
        {
            var info = new AiMemoryInfo();
            if (scene != IntPtr.Zero)
            {
                aiGetMemoryRequirements(scene, ref info);
            }

            return info;
        }

        #endregion

        #region Version Info

        /// <summary>
        /// Gets the Assimp legal info.
        /// </summary>
        /// <returns>String containing Assimp legal info.</returns>
        public static string GetLegalString()
        {
            var ptr = aiGetLegalString();

            if (ptr == IntPtr.Zero)
                return string.Empty;

            return Marshal.PtrToStringAnsi(ptr);
        }

        /// <summary>
        /// Gets the native Assimp DLL's minor version number.
        /// </summary>
        /// <returns>Assimp minor version number</returns>
        public static uint GetVersionMinor()
        {
            return aiGetVersionMinor();
        }

        /// <summary>
        /// Gets the native Assimp DLL's major version number.
        /// </summary>
        /// <returns>Assimp major version number</returns>
        public static uint GetVersionMajor()
        {
            return aiGetVersionMajor();
        }

        /// <summary>
        /// Gets the native Assimp DLL's revision version number.
        /// </summary>
        /// <returns>Assimp revision version number</returns>
        public static uint GetVersionRevision()
        {
            return aiGetVersionRevision();
        }

        /// <summary>
        /// Returns the branchname of the Assimp runtime.
        /// </summary>
        /// <returns>The current branch name.</returns>
        public static string GetBranchName()
        {
            var ptr = aiGetBranchName();

            if (ptr == IntPtr.Zero)
                return string.Empty;

            return Marshal.PtrToStringAnsi(ptr);
        }

        /// <summary>
        /// Gets the native Assimp DLL's current version number as "major.minor.revision" string. This is the
        /// version of Assimp that this wrapper is currently using.
        /// </summary>
        /// <returns>Unmanaged DLL version</returns>
        public static string GetVersion()
        {
            var major = GetVersionMajor();
            var minor = GetVersionMinor();
            var rev = GetVersionRevision();

            return $"{major}.{minor}.{rev}";
        }

        /// <summary>
        /// Gets the native Assimp DLL's current version number as a .NET version object.
        /// </summary>
        /// <returns>Unmanaged DLL version</returns>
        public static Version GetVersionAsVersion()
        {
            return new Version((int)GetVersionMajor(), (int)GetVersionMinor(), 0, (int)GetVersionRevision());
        }

        /// <summary>
        /// Get the compilation flags that describe how the native Assimp DLL was compiled.
        /// </summary>
        /// <returns>Compilation flags</returns>
        public static CompileFlags GetCompileFlags() { return (CompileFlags)aiGetCompileFlags(); }


        #endregion

        /// <summary>
        /// Gets an embedded texture.
        /// </summary>
        /// <param name="scene">Input asset.</param>
        /// <param name="filename">Texture path extracted from <see cref="GetMaterialString"/>.</param>
        /// <returns>An embedded texture, or nullptr.</returns>
        public static IntPtr GetEmbeddedTexture(IntPtr scene, string filename)
        {
            if (scene == IntPtr.Zero)
                return IntPtr.Zero;

            return aiGetEmbeddedTexture(scene, filename);
        }

        #region LibraryImport

        // Import

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiImportFile(string file, uint flags);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiImportFileEx(string file, uint flags, IntPtr fileIO);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiImportFileFromMemory([In] byte[] buffer, uint bufferLength, uint flags, string formatHint);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiImportFileExWithProperties(string file, uint flag, IntPtr fileIO, IntPtr propStore);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiImportFileFromMemoryWithProperties([In] byte[] buffer, uint bufferLength, uint flags, string formatHint, IntPtr propStore);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiReleaseImport(IntPtr scene);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiApplyPostProcessing(IntPtr scene, uint Flags);

        // Export

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial UIntPtr aiGetExportFormatCount();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetExportFormatDescription(UIntPtr index);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiReleaseExportFormatDescription(IntPtr desc);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiExportSceneToBlob(IntPtr scene, string formatId, uint preProcessing);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiReleaseExportBlob(IntPtr blobData);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiExportScene(IntPtr scene, string formatId, string fileName, uint preProcessing);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiExportSceneEx(IntPtr scene, string formatId, string fileName, IntPtr fileIO, uint preProcessing);        /// <summary>

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiCopyScene(IntPtr sceneIn, out IntPtr sceneOut);

        // Logging

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiAttachLogStream(IntPtr logStreamPtr);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiEnableVerboseLogging([MarshalAs(UnmanagedType.Bool)] bool enable);        /// Defines all of the delegates that represent the unmanaged assimp functions.

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiDetachLogStream(IntPtr logStreamPtr);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiDetachAllLogStreams();

        // Property

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiCreatePropertyStore();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiReleasePropertyStore(IntPtr propertyStore);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiSetImportPropertyInteger(IntPtr propertyStore, string name, int value);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiSetImportPropertyFloat(IntPtr propertyStore, string name, float value);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiSetImportPropertyString(IntPtr propertyStore, string name, ref AiString value);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiSetImportPropertyMatrix(IntPtr propertyStore, string name, ref Matrix4x4 value);

        // Material

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialColor(ref AiMaterial mat, string key, uint texType, uint texIndex, IntPtr colorOut);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialFloatArray(ref AiMaterial mat, string key, uint texType, uint texIndex, IntPtr ptrOut, ref uint valueCount);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialIntegerArray(ref AiMaterial mat, string key, uint texType, uint texIndex, IntPtr ptrOut, ref uint valueCount);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialProperty(ref AiMaterial mat, string key, uint texType, uint texIndex, out IntPtr propertyOut);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialString(ref AiMaterial mat, string key, uint texType, uint texIndex, out AiString str);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial ReturnCode aiGetMaterialTexture(ref AiMaterial mat, TextureType type, uint index, out AiString path, out TextureMapping mapping, out uint uvIndex, out float blendFactor, out TextureOperation textureOp, [In, Out] TextureWrapMode[] wrapModes, out uint flags);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial uint aiGetMaterialTextureCount(ref AiMaterial mat, TextureType type);

        // Error and Info

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetErrorString();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiGetExtensionList(ref AiString extensionsOut);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial void aiGetMemoryRequirements(IntPtr scene, ref AiMemoryInfo memoryInfo);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool aiIsExtensionSupported(string extension);

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial UIntPtr aiGetImportFormatCount();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetImportFormatDescription(UIntPtr index);

        // Version Info

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetLegalString();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial uint aiGetVersionMinor();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial uint aiGetVersionMajor();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial uint aiGetVersionRevision();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetBranchName();

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial uint aiGetCompileFlags();

        //

        [LibraryImport(DLL_NAME, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr aiGetEmbeddedTexture(IntPtr scene, string filename);

        #endregion

        // Assimp's quaternions are WXYZ, C#'s are XYZW, we need to convert all of them.
        internal static Quaternion FixQuaternionFromAssimp(Quaternion quat) => new(quat.Y, quat.Z, quat.W, quat.X);
        internal static Quaternion FixQuaternionToAssimp(Quaternion quat) => new(quat.W, quat.X, quat.Y, quat.Z);
        internal static unsafe void FixQuaternionsInSceneFromAssimp(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return;

            var scene = (AiScene*)ptr;
            if (scene->NumAnimations == 0)
                return;

            for (uint i = 0; i < scene->NumAnimations; i++)
            {
                var anim = ((AiAnimation**)scene->Animations)[i];
                for (uint j = 0; j < anim->NumChannels; j++)
                {
                    var channel = ((AiNodeAnim**)anim->Channels)[j];
                    for (uint k = 0; k < channel->NumRotationKeys; k++)
                    {
                        ref var rotKey = ref ((QuaternionKey*)channel->RotationKeys)[k];
                        rotKey.Value = FixQuaternionFromAssimp(rotKey.Value);
                    }
                }
            }
        }
        internal static unsafe void FixQuaternionsInSceneToAssimp(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return;

            var scene = (AiScene*)ptr;
            if (scene->NumAnimations == 0)
                return;

            for (uint i = 0; i < scene->NumAnimations; i++)
            {
                var anim = ((AiAnimation**)scene->Animations)[i];
                for (uint j = 0; j < anim->NumChannels; j++)
                {
                    var channel = ((AiNodeAnim**)anim->Channels)[j];
                    for (uint k = 0; k < channel->NumRotationKeys; k++)
                    {
                        ref var rotKey = ref ((QuaternionKey*)channel->RotationKeys)[k];
                        rotKey.Value = FixQuaternionToAssimp(rotKey.Value);
                    }
                }
            }
        }
    }
}
